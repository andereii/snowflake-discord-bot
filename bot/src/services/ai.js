import axios from 'axios';
import { SYSTEM_PROMPT } from './systemPrompt.js';
import { getToolsForDeepSeek, getToolsForGemini, getToolByName } from './aiTools.js';

// Per-guild conversation history
// Historial stores items normalized:
// { type: 'message', role: 'user'|'assistant', content: string }
// { type: 'function_call', call_id: string, name: string, arguments: string }
// { type: 'function_call_output', call_id: string, name: string, output: string }
// { type: 'web_search_call' }
const history = new Map();

// Track bot-generated AI message IDs -> guildId so we detect replies
const generatedMessages = new Map();

export function registerGeneratedMessage(messageId, guildId) {
    generatedMessages.set(messageId, guildId);
}

export function isGeneratedMessage(messageId) {
    return generatedMessages.has(messageId);
}

export function getGeneratedMessageGuild(messageId) {
    return generatedMessages.get(messageId);
}

/**
 * @typedef {Object} AiOutcome
 * @property {string} text - Final text response
 * @property {Array<{ success: boolean, text: string, description: string }>} commands - Executed command results
 * @property {Object|null} pending - Pending destructive command requiring button confirmation
 * @property {boolean} usedWebSearch - Whether web search was triggered
 */

/**
 * @param {Object} ctx - { client, guild, channel, member }
 * @param {string} userName
 * @param {string} text
 * @param {object} [opts]
 * @param {string} [opts.systemPrompt]
 * @param {boolean} [opts.webSearchEnabled]
 * @param {boolean} [opts.commandsEnabled]
 * @param {Function} [opts.onSearching]
 * @returns {Promise<AiOutcome>}
 */
export async function askAi(ctx, userName, text, opts = {}) {
    const guildId = ctx.guild.id;
    const {
        systemPrompt = SYSTEM_PROMPT,
        webSearchEnabled = true,
        commandsEnabled = true,
        onSearching = null,
    } = opts;

    if (!history.has(guildId)) {
        history.set(guildId, []);
    }
    const guildHistory = history.get(guildId);

    guildHistory.push({ type: 'message', role: 'user', content: `[${userName}] ${text}` });
    trimHistory(guildHistory, 5);

    return await executeAiLoop(ctx, guildHistory, {
        systemPrompt,
        webSearchEnabled,
        commandsEnabled,
        onSearching
    });
}

/**
 * Resume AI turn after a pending command is confirmed or rejected
 */
export async function resumeAiTool(ctx, { callId, toolName, output }, opts = {}) {
    const guildId = ctx.guild.id;
    const guildHistory = history.get(guildId) || [];

    // Append function call output
    guildHistory.push({
        type: 'function_call_output',
        call_id: callId,
        name: toolName,
        output: output
    });

    trimHistory(guildHistory, 5);

    return await executeAiLoop(ctx, guildHistory, {
        systemPrompt: opts.systemPrompt || SYSTEM_PROMPT,
        webSearchEnabled: opts.webSearchEnabled ?? true,
        commandsEnabled: opts.commandsEnabled ?? true,
        onSearching: opts.onSearching || null
    });
}

async function executeAiLoop(ctx, guildHistory, opts) {
    const maxIterations = 5;
    const executedCommands = [];
    let usedWebSearch = false;

    let primaryProvider = process.env.AI_PROVIDER
        || (process.env.DEEPSEEK_API_KEY ? 'deepseek' : (process.env.GEMINI_API_KEY ? 'gemini' : null));
    let secondaryProvider = primaryProvider === 'deepseek'
        ? (process.env.GEMINI_API_KEY ? 'gemini' : null)
        : (process.env.DEEPSEEK_API_KEY ? 'deepseek' : null);

    if (!primaryProvider) {
        return {
            text: "No hay ninguna clave de API (DeepSeek o Gemini) configurada en el bot.",
            commands: [],
            pending: null,
            usedWebSearch: false
        };
    }

    let activeProvider = primaryProvider;
    let fallbackInfo = null;

    for (let iter = 0; iter < maxIterations; iter++) {
        let resp;
        try {
            if (activeProvider === 'deepseek') {
                resp = await callDeepSeek(guildHistory, opts);
            } else {
                resp = await callGemini(guildHistory, opts);
            }
        } catch (error) {
            const errorMsg = error.response?.data?.error?.message
                || error.response?.data?.error
                || error.message
                || 'Error desconocido';

            console.warn(`[ai] Error en proveedor principal (${activeProvider}):`, errorMsg);

            // Try fallback to secondary provider if available
            if (secondaryProvider && activeProvider !== secondaryProvider) {
                fallbackInfo = {
                    from: activeProvider,
                    to: secondaryProvider,
                    reason: errorMsg
                };

                if (opts.onFallback) {
                    await opts.onFallback(fallbackInfo).catch(() => {});
                }

                activeProvider = secondaryProvider;
                try {
                    if (activeProvider === 'deepseek') {
                        resp = await callDeepSeek(guildHistory, opts);
                    } else {
                        resp = await callGemini(guildHistory, opts);
                    }
                } catch (secError) {
                    const secMsg = secError.response?.data?.error?.message || secError.message || 'Error desconocido';
                    console.error(`[ai] Error en proveedor secundario (${activeProvider}):`, secMsg);
                    return {
                        text: "Hubo un error al contactar a los proveedores de IA.",
                        commands: executedCommands,
                        pending: null,
                        usedWebSearch,
                        fallbackInfo: {
                            from: `${fallbackInfo.from} & ${activeProvider}`,
                            to: null,
                            reason: `${errorMsg} | Secundario: ${secMsg}`
                        }
                    };
                }
            } else {
                return {
                    text: "Hubo un error al contactar a la IA.",
                    commands: executedCommands,
                    pending: null,
                    usedWebSearch,
                    fallbackInfo: { from: activeProvider, to: null, reason: errorMsg }
                };
            }
        }

        if (resp.usedWebSearch) {
            usedWebSearch = true;
            if (opts.onSearching) await opts.onSearching();
        }

        // Add model output items into normalized history
        guildHistory.push(...resp.outputItems);

        // If no tool calls, return final text
        if (!resp.functionCalls || resp.functionCalls.length === 0) {
            let finalOutput = resp.text || '';
            if (finalOutput.length > 2000) {
                finalOutput = finalOutput.substring(0, 1997) + '...';
            }
            return {
                text: finalOutput,
                commands: executedCommands,
                pending: null,
                usedWebSearch
            };
        }

        // Handle tool calls
        for (const call of resp.functionCalls) {
            const tool = getToolByName(call.name);
            let parsedArgs = {};
            try {
                parsedArgs = typeof call.args === 'string' ? JSON.parse(call.args) : (call.args || {});
            } catch {
                parsedArgs = {};
            }

            if (!tool) {
                guildHistory.push({
                    type: 'function_call_output',
                    call_id: call.callId,
                    name: call.name,
                    output: `Error: herramienta "${call.name}" desconocida.`
                });
                continue;
            }

            // If destructive, interrupt loop and return pending command for button confirmation
            if (tool.destructive) {
                return {
                    text: resp.text || null,
                    commands: executedCommands,
                    pending: {
                        toolName: call.name,
                        args: parsedArgs,
                        callId: call.callId,
                    },
                    usedWebSearch
                };
            }

            // Execute non-destructive tool
            try {
                const res = await tool.execute(ctx, parsedArgs);
                executedCommands.push(res);
                guildHistory.push({
                    type: 'function_call_output',
                    call_id: call.callId,
                    name: call.name,
                    output: res.text
                });
            } catch (err) {
                console.error(`[ai] Error executing tool ${call.name}:`, err);
                guildHistory.push({
                    type: 'function_call_output',
                    call_id: call.callId,
                    name: call.name,
                    output: `Error ejecutando comando: ${err.message}`
                });
            }
        }
    }

    return {
        text: "…",
        commands: executedCommands,
        pending: null,
        usedWebSearch
    };
}

// ──────────────────────────────────────────────
// DeepSeek Responses API
// ──────────────────────────────────────────────

async function callDeepSeek(guildHistory, opts) {
    const tools = [];
    if (opts.webSearchEnabled) {
        tools.push({ type: 'web_search' });
    }
    if (opts.commandsEnabled) {
        tools.push(...getToolsForDeepSeek());
    }

    // Convert normalized history to DeepSeek input
    const input = guildHistory.map(item => {
        if (item.type === 'message') {
            return { type: 'message', role: item.role, content: item.content };
        } else if (item.type === 'function_call') {
            return { type: 'function_call', call_id: item.call_id, name: item.name, arguments: item.arguments };
        } else if (item.type === 'function_call_output') {
            return { type: 'function_call_output', call_id: item.call_id, output: item.output };
        } else if (item.type === 'web_search_call') {
            return null;
        }
    }).filter(Boolean);

    const payload = {
        model: process.env.DEEPSEEK_MODEL || 'deepseek-chat',
        instructions: opts.systemPrompt,
        input,
        temperature: 0.7,
        max_output_tokens: 512,
        stream: false
    };

    if (tools.length > 0) {
        payload.tools = tools;
        payload.tool_choice = 'auto';
    }

    const response = await axios.post('https://api.deepseek.com/responses', payload, {
        headers: {
            'Authorization': `Bearer ${process.env.DEEPSEEK_API_KEY}`,
            'Content-Type': 'application/json'
        },
        timeout: 60000
    });

    const data = response.data;
    let text = '';
    const functionCalls = [];
    const outputItems = [];
    let usedWebSearch = false;

    if (data.output && Array.isArray(data.output)) {
        for (const item of data.output) {
            if (item.type === 'web_search_call') {
                usedWebSearch = true;
                outputItems.push({ type: 'web_search_call' });
            } else if (item.type === 'function_call') {
                functionCalls.push({
                    callId: item.call_id || item.name,
                    name: item.name,
                    args: item.arguments
                });
                outputItems.push({
                    type: 'function_call',
                    call_id: item.call_id || item.name,
                    name: item.name,
                    arguments: typeof item.arguments === 'string' ? item.arguments : JSON.stringify(item.arguments)
                });
            } else if (item.type === 'message' && item.content) {
                const chunk = typeof item.content === 'string' ? item.content : item.content.map(c => c.text).join('');
                text += chunk;
                outputItems.push({ type: 'message', role: 'assistant', content: chunk });
            }
        }
    }

    return { text, functionCalls, outputItems, usedWebSearch };
}

// ──────────────────────────────────────────────
// Gemini API
// ──────────────────────────────────────────────

async function callGemini(guildHistory, opts) {
    const geminiModel = process.env.GEMINI_MODEL || 'gemini-3.6-flash';
    const tools = [];

    if (opts.webSearchEnabled) {
        tools.push({ google_search: {} });
    }
    if (opts.commandsEnabled) {
        tools.push({ function_declarations: getToolsForGemini() });
    }

    // Convert normalized history to Gemini contents format
    const contents = [];
    let currentRole = null;
    let currentParts = [];

    function flush() {
        if (currentRole && currentParts.length > 0) {
            contents.push({ role: currentRole, parts: currentParts });
        }
        currentRole = null;
        currentParts = [];
    }

    for (const item of guildHistory) {
        if (item.type === 'web_search_call') continue;

        if (item.type === 'message') {
            const role = item.role === 'user' ? 'user' : 'model';
            if (currentRole !== role) {
                flush();
                currentRole = role;
            }
            currentParts.push({ text: item.content });
        } else if (item.type === 'function_call') {
            if (currentRole !== 'model') {
                flush();
                currentRole = 'model';
            }
            let argsObj = {};
            try { argsObj = JSON.parse(item.arguments); } catch {}
            currentParts.push({ functionCall: { name: item.name, args: argsObj } });
        } else if (item.type === 'function_call_output') {
            if (currentRole !== 'user') {
                flush();
                currentRole = 'user';
            }
            currentParts.push({
                functionResponse: {
                    name: item.name,
                    response: { output: item.output }
                }
            });
        }
    }
    flush();

    const payload = {
        contents,
        systemInstruction: { parts: [{ text: opts.systemPrompt }] },
        generationConfig: { temperature: 0.7, maxOutputTokens: 512 }
    };

    if (tools.length > 0) {
        payload.tools = tools;
        payload.tool_config = {
            function_calling_config: { mode: 'AUTO' }
        };
    }

    const response = await axios.post(
        `https://generativelanguage.googleapis.com/v1beta/models/${geminiModel}:generateContent?key=${process.env.GEMINI_API_KEY}`,
        payload,
        { headers: { 'Content-Type': 'application/json' }, timeout: 60000 }
    );

    let text = '';
    const functionCalls = [];
    const outputItems = [];
    let usedWebSearch = false;

    const candidate = response.data?.candidates?.[0];
    if (candidate?.groundingMetadata?.groundingChunks?.length > 0) {
        usedWebSearch = true;
    }

    if (candidate?.content?.parts) {
        for (const part of candidate.content.parts) {
            if (part.text) {
                text += part.text;
                outputItems.push({ type: 'message', role: 'assistant', content: part.text });
            } else if (part.functionCall) {
                const callId = `gemini_${Date.now()}_${Math.random().toString(36).substring(2, 7)}`;
                functionCalls.push({
                    callId,
                    name: part.functionCall.name,
                    args: part.functionCall.args || {}
                });
                outputItems.push({
                    type: 'function_call',
                    call_id: callId,
                    name: part.functionCall.name,
                    arguments: JSON.stringify(part.functionCall.args || {})
                });
            }
        }
    }

    return { text, functionCalls, outputItems, usedWebSearch };
}

function trimHistory(guildHistory, maxTurns) {
    let userCount = guildHistory.filter(m => m.type === 'message' && m.role === 'user').length;
    while (userCount > maxTurns && guildHistory.length > 0) {
        const removed = guildHistory.shift();
        if (removed.type === 'message' && removed.role === 'user') {
            userCount--;
        }
    }
}

export function clearHistory(guildId) {
    if (history.has(guildId)) {
        history.delete(guildId);
        for (const [msgId, gId] of generatedMessages) {
            if (gId === guildId) generatedMessages.delete(msgId);
        }
        return true;
    }
    return false;
}
