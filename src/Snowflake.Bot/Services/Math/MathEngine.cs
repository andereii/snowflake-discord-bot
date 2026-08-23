using System.Globalization;
using System.Text;
using System.Text.RegularExpressions;

namespace Snowflake.Bot.Services.Calculators;

public sealed record MathResult(
    bool Exitoso,
    double Resultado,
    string ExpresionNormalizada,
    string? FraccionExacta,
    string? ErrorClave,
    string? ErrorDetalle);

/// <summary>
/// Motor de evaluación matemática científica en memoria.
/// Soporta: PEMDAS estricto, (), [], {}, multiplicación implícita, !, raices, logaritmos,
/// trigonometría, constantes pi/e/tau/phi y simplificación a fracciones irreducibles P/Q.
/// </summary>
public static class MathEngine
{
    private static readonly HashSet<string> FuncionesConocidas = new(StringComparer.OrdinalIgnoreCase)
    {
        "sin", "cos", "tan", "asin", "arcsin", "acos", "arccos", "atan", "arctan", "atan2",
        "sinh", "cosh", "tanh", "asinh", "acosh", "atanh",
        "sqrt", "cbrt", "root", "nthroot",
        "log", "log10", "log2", "ln", "exp",
        "abs", "fact", "factorial", "gamma",
        "deg", "rad", "floor", "ceil", "round", "sign"
    };

    private static readonly HashSet<string> ConstantesConocidas = new(StringComparer.OrdinalIgnoreCase)
    {
        "pi", "e", "tau", "phi"
    };

    /// <summary>
    /// Determina si una entrada es una consulta en lenguaje natural (para IA)
    /// o una expresión matemática formal para evaluar localmente.
    /// </summary>
    public static bool EsLenguajeNatural(string entrada)
    {
        if (string.IsNullOrWhiteSpace(entrada)) return false;
        entrada = entrada.Trim();

        if (entrada.Contains('?') || entrada.Contains('¿') || entrada.Contains('='))
            return true;

        // Extraer palabras
        var palabras = Regex.Matches(entrada, @"[a-zA-ZáéíóúÁÉÍÓÚñÑ_]+");
        foreach (Match p in palabras)
        {
            var pal = p.Value.ToLowerInvariant();
            if (FuncionesConocidas.Contains(pal) || ConstantesConocidas.Contains(pal))
                continue;

            // Si contiene palabras no matemáticas como "cuanto", "hola", "si", "tengo", "derivada", "integral"
            if (pal.Length > 1) return true;
        }

        return false;
    }

    /// <summary>
    /// Evalúa una expresión matemática y retorna el resultado o error específico.
    /// </summary>
    public static MathResult Evaluar(string? entrada)
    {
        if (string.IsNullOrWhiteSpace(entrada))
            return new MathResult(false, 0, "", null, "Calculadora:ErrorSintaxis", "Expresión vacía");

        var normalizada = Normalizar(entrada);

        try
        {
            var tokens = Tokenizar(normalizada);
            if (tokens.Count == 0)
                return new MathResult(false, 0, normalizada, null, "Calculadora:ErrorSintaxis", "Sin tokens");

            var rpn = ConvertirShuntingYard(tokens);
            var valor = EvaluarRpn(rpn);

            if (double.IsNaN(valor) || double.IsInfinity(valor))
            {
                return new MathResult(false, 0, normalizada, null, "Calculadora:DivisionPorCero", null);
            }

            var fraccion = CalcularFraccionExacta(valor);

            return new MathResult(true, valor, normalizada, fraccion, null, null);
        }
        catch (MathDomainException ex)
        {
            return new MathResult(false, 0, normalizada, null, "Calculadora:ErrorDominio", ex.Message);
        }
        catch (DivideByZeroException)
        {
            return new MathResult(false, 0, normalizada, null, "Calculadora:DivisionPorCero", null);
        }
        catch (FactorialException ex)
        {
            return new MathResult(false, 0, normalizada, null, "Calculadora:ErrorFactorial", ex.Message);
        }
        catch (MathSyntaxException ex)
        {
            return new MathResult(false, 0, normalizada, null, "Calculadora:ErrorSintaxis", ex.Message);
        }
        catch (Exception ex)
        {
            return new MathResult(false, 0, normalizada, null, "Calculadora:ErrorDesconocido", ex.Message);
        }
    }

    private static string Normalizar(string expr)
    {
        var sb = new StringBuilder(expr.Length);
        foreach (var ch in expr)
        {
            sb.Append(ch switch
            {
                '[' or '{' => '(',
                ']' or '}' => ')',
                '×' => '*',
                '÷' or ':' => '/',
                'π' => "pi",
                'τ' => "tau",
                'ϕ' => "phi",
                '—' or '–' => '-',
                ',' => ',',
                _ => ch
            });
        }
        return sb.ToString().Replace("**", "^").Trim();
    }

    // -------------------------------------------------------------
    // Tokenizador con Multiplicación Implícita
    // -------------------------------------------------------------

    private enum TokenType
    {
        Number,
        Identifier,
        Plus,
        Minus,
        Multiply,
        Divide,
        Modulo,
        Power,
        Factorial,
        OpenParen,
        CloseParen,
        Comma
    }

    private sealed record Token(TokenType Type, string Text, double Value = 0, int ArgCount = 1);

    private static List<Token> Tokenizar(string expr)
    {
        var rawTokens = new List<Token>();
        int i = 0;

        while (i < expr.Length)
        {
            char c = expr[i];

            if (char.IsWhiteSpace(c))
            {
                i++;
                continue;
            }

            if (char.IsDigit(c) || (c == '.' && i + 1 < expr.Length && char.IsDigit(expr[i + 1])))
            {
                int start = i;
                while (i < expr.Length && (char.IsDigit(expr[i]) || expr[i] == '.'))
                    i++;

                var numStr = expr[start..i];
                if (!double.TryParse(numStr, NumberStyles.Float, CultureInfo.InvariantCulture, out var num))
                    throw new MathSyntaxException($"Número inválido: '{numStr}'");

                rawTokens.Add(new Token(TokenType.Number, numStr, num));
                continue;
            }

            if (char.IsLetter(c))
            {
                int start = i;
                while (i < expr.Length && (char.IsLetterOrDigit(expr[i]) || expr[i] == '_'))
                    i++;

                var id = expr[start..i];
                rawTokens.Add(new Token(TokenType.Identifier, id));
                continue;
            }

            switch (c)
            {
                case '+': rawTokens.Add(new Token(TokenType.Plus, "+")); break;
                case '-': rawTokens.Add(new Token(TokenType.Minus, "-")); break;
                case '*': rawTokens.Add(new Token(TokenType.Multiply, "*")); break;
                case '/': rawTokens.Add(new Token(TokenType.Divide, "/")); break;
                case '%': rawTokens.Add(new Token(TokenType.Modulo, "%")); break;
                case '^': rawTokens.Add(new Token(TokenType.Power, "^")); break;
                case '!': rawTokens.Add(new Token(TokenType.Factorial, "!")); break;
                case '(': rawTokens.Add(new Token(TokenType.OpenParen, "(")); break;
                case ')': rawTokens.Add(new Token(TokenType.CloseParen, ")")); break;
                case ',': rawTokens.Add(new Token(TokenType.Comma, ",")); break;
                default:
                    throw new MathSyntaxException($"Carácter desconocido: '{c}'");
            }
            i++;
        }

        // Inserción de multiplicación implícita:
        // 1. Number -> OpenParen:  3(4) -> 3 * (4)
        // 2. Number -> Identifier: 2pi -> 2 * pi, 5sqrt(4) -> 5 * sqrt(4)
        // 3. CloseParen -> OpenParen: (2+3)(4+5) -> (2+3) * (4+5)
        // 4. CloseParen -> Number / Identifier: (2)3 -> (2) * 3, (2)pi -> (2) * pi
        // 5. Factorial -> Number / Identifier / OpenParen: 5! 2 -> 5! * 2
        var tokens = new List<Token>(rawTokens.Count * 2);
        for (int k = 0; k < rawTokens.Count; k++)
        {
            var actual = rawTokens[k];
            tokens.Add(actual);

            if (k + 1 < rawTokens.Count)
            {
                var sig = rawTokens[k + 1];
                bool insertar = false;

                if (actual.Type == TokenType.Number && (sig.Type == TokenType.OpenParen || sig.Type == TokenType.Identifier))
                    insertar = true;
                else if (actual.Type == TokenType.CloseParen && (sig.Type == TokenType.OpenParen || sig.Type == TokenType.Number || sig.Type == TokenType.Identifier))
                    insertar = true;
                else if (actual.Type == TokenType.Factorial && (sig.Type == TokenType.Number || sig.Type == TokenType.Identifier || sig.Type == TokenType.OpenParen))
                    insertar = true;
                else if (actual.Type == TokenType.Identifier && ConstantesConocidas.Contains(actual.Text) &&
                    (sig.Type == TokenType.OpenParen || sig.Type == TokenType.Number || (sig.Type == TokenType.Identifier && ConstantesConocidas.Contains(sig.Text))))
                    insertar = true;

                if (insertar)
                {
                    tokens.Add(new Token(TokenType.Multiply, "*"));
                }
            }
        }

        return tokens;
    }

    // -------------------------------------------------------------
    // Shunting-Yard Algorithm (Infix -> RPN)
    // -------------------------------------------------------------

    private static int ObtenerPrecedencia(Token t, bool unario = false) => t.Type switch
    {
        TokenType.Plus or TokenType.Minus => unario ? 5 : 1,
        TokenType.Multiply or TokenType.Divide or TokenType.Modulo => 2,
        TokenType.Power => 4,
        TokenType.Factorial => 6,
        _ => 0
    };

    private static bool EsAsociatividadDerecha(Token t, bool unario = false)
        => unario || t.Type == TokenType.Power;

    private static List<Token> ConvertirShuntingYard(List<Token> tokens)
    {
        var salida = new List<Token>();
        var pilaOperadores = new Stack<Token>();
        var pilaArgCount = new Stack<int>();

        bool esperaOperando = true;

        for (int i = 0; i < tokens.Count; i++)
        {
            var t = tokens[i];

            if (t.Type == TokenType.Number)
            {
                salida.Add(t);
                esperaOperando = false;
            }
            else if (t.Type == TokenType.Identifier)
            {
                if (ConstantesConocidas.Contains(t.Text))
                {
                    salida.Add(t);
                    esperaOperando = false;
                }
                else if (FuncionesConocidas.Contains(t.Text))
                {
                    pilaOperadores.Push(t);
                    pilaArgCount.Push(1);
                    esperaOperando = true;
                }
                else
                {
                    throw new MathSyntaxException($"Elemento desconocido: '{t.Text}'");
                }
            }
            else if (t.Type == TokenType.Comma)
            {
                while (pilaOperadores.Count > 0 && pilaOperadores.Peek().Type != TokenType.OpenParen)
                    salida.Add(pilaOperadores.Pop());

                if (pilaOperadores.Count == 0)
                    throw new MathSyntaxException("Coma fuera de función o paréntesis no balanceados.");

                if (pilaArgCount.Count > 0)
                {
                    var count = pilaArgCount.Pop();
                    pilaArgCount.Push(count + 1);
                }
                esperaOperando = true;
            }
            else if (t.Type == TokenType.Factorial)
            {
                // Factorial es postfijo
                salida.Add(t);
                esperaOperando = false;
            }
            else if (t.Type is TokenType.Plus or TokenType.Minus or TokenType.Multiply or TokenType.Divide or TokenType.Modulo or TokenType.Power)
            {
                bool esUnario = esperaOperando && t.Type is TokenType.Plus or TokenType.Minus;
                var opToken = esUnario ? new Token(t.Type, "u" + t.Text) : t;

                while (pilaOperadores.Count > 0)
                {
                    var tope = pilaOperadores.Peek();
                    if (tope.Type == TokenType.OpenParen) break;

                    int precActual = ObtenerPrecedencia(opToken, esUnario);
                    int precTope = ObtenerPrecedencia(tope, tope.Text.StartsWith('u'));

                    if ((!EsAsociatividadDerecha(opToken, esUnario) && precActual <= precTope)
                        || (EsAsociatividadDerecha(opToken, esUnario) && precActual < precTope))
                    {
                        salida.Add(pilaOperadores.Pop());
                    }
                    else break;
                }

                pilaOperadores.Push(opToken);
                esperaOperando = true;
            }
            else if (t.Type == TokenType.OpenParen)
            {
                pilaOperadores.Push(t);
                esperaOperando = true;
            }
            else if (t.Type == TokenType.CloseParen)
            {
                while (pilaOperadores.Count > 0 && pilaOperadores.Peek().Type != TokenType.OpenParen)
                    salida.Add(pilaOperadores.Pop());

                if (pilaOperadores.Count == 0)
                    throw new MathSyntaxException("Paréntesis no balanceados: falta '('");

                pilaOperadores.Pop(); // descartar '('

                if (pilaOperadores.Count > 0 && pilaOperadores.Peek().Type == TokenType.Identifier)
                {
                    var func = pilaOperadores.Pop();
                    var argC = pilaArgCount.Count > 0 ? pilaArgCount.Pop() : 1;
                    salida.Add(new Token(TokenType.Identifier, func.Text, ArgCount: argC));
                }

                esperaOperando = false;
            }
        }

        while (pilaOperadores.Count > 0)
        {
            var op = pilaOperadores.Pop();
            if (op.Type is TokenType.OpenParen or TokenType.CloseParen)
                throw new MathSyntaxException("Paréntesis no balanceados.");
            salida.Add(op);
        }

        return salida;
    }

    // -------------------------------------------------------------
    // Evaluador RPN
    // -------------------------------------------------------------

    private static double EvaluarRpn(List<Token> rpn)
    {
        var pila = new Stack<double>();

        foreach (var t in rpn)
        {
            if (t.Type == TokenType.Number)
            {
                pila.Push(t.Value);
            }
            else if (t.Type == TokenType.Identifier && ConstantesConocidas.Contains(t.Text))
            {
                pila.Push(t.Text.ToLowerInvariant() switch
                {
                    "pi" => System.Math.PI,
                    "e" => System.Math.E,
                    "tau" => System.Math.Tau,
                    "phi" => 1.6180339887498948482,
                    _ => 0
                });
            }
            else if (t.Text == "u+")
            {
                // No hace nada
            }
            else if (t.Text == "u-")
            {
                if (pila.Count < 1) throw new MathSyntaxException("Operador unario sin valor.");
                pila.Push(-pila.Pop());
            }
            else if (t.Type == TokenType.Factorial)
            {
                if (pila.Count < 1) throw new MathSyntaxException("Factorial sin valor.");
                pila.Push(CalcularFactorial(pila.Pop()));
            }
            else if (t.Type is TokenType.Plus or TokenType.Minus or TokenType.Multiply or TokenType.Divide or TokenType.Modulo or TokenType.Power)
            {
                if (pila.Count < 2) throw new MathSyntaxException($"Operador '{t.Text}' requiere dos operandos.");
                var b = pila.Pop();
                var a = pila.Pop();

                pila.Push(t.Type switch
                {
                    TokenType.Plus => a + b,
                    TokenType.Minus => a - b,
                    TokenType.Multiply => a * b,
                    TokenType.Divide => b == 0 ? throw new DivideByZeroException() : a / b,
                    TokenType.Modulo => b == 0 ? throw new DivideByZeroException() : a % b,
                    TokenType.Power => System.Math.Pow(a, b),
                    _ => 0
                });
            }
            else if (t.Type == TokenType.Identifier && FuncionesConocidas.Contains(t.Text))
            {
                int argCount = t.ArgCount;
                var fn = t.Text.ToLowerInvariant();

                if (argCount == 1)
                {
                    if (pila.Count < 1) throw new MathSyntaxException($"Función '{fn}' requiere 1 argumento.");
                    var x = pila.Pop();

                    pila.Push(fn switch
                    {
                        "sin" => System.Math.Sin(x),
                        "cos" => System.Math.Cos(x),
                        "tan" => System.Math.Tan(x),
                        "asin" or "arcsin" when x is < -1 or > 1 => throw new MathDomainException("asin requiere x entre -1 y 1."),
                        "asin" or "arcsin" => System.Math.Asin(x),
                        "acos" or "arccos" when x is < -1 or > 1 => throw new MathDomainException("acos requiere x entre -1 y 1."),
                        "acos" or "arccos" => System.Math.Acos(x),
                        "atan" or "arctan" => System.Math.Atan(x),
                        "sinh" => System.Math.Sinh(x),
                        "cosh" => System.Math.Cosh(x),
                        "tanh" => System.Math.Tanh(x),
                        "asinh" => System.Math.Asinh(x),
                        "acosh" when x < 1 => throw new MathDomainException("acosh requiere x >= 1."),
                        "acosh" => System.Math.Acosh(x),
                        "atanh" when x is <= -1 or >= 1 => throw new MathDomainException("atanh requiere -1 < x < 1."),
                        "atanh" => System.Math.Atanh(x),
                        "sqrt" when x < 0 => throw new MathDomainException("sqrt no acepta números negativos en reales."),
                        "sqrt" => System.Math.Sqrt(x),
                        "cbrt" => System.Math.Cbrt(x),
                        "ln" when x <= 0 => throw new MathDomainException("ln requiere x > 0."),
                        "ln" => System.Math.Log(x),
                        "log" or "log10" when x <= 0 => throw new MathDomainException("log requiere x > 0."),
                        "log" or "log10" => System.Math.Log10(x),
                        "log2" when x <= 0 => throw new MathDomainException("log2 requiere x > 0."),
                        "log2" => System.Math.Log2(x),
                        "exp" => System.Math.Exp(x),
                        "abs" => System.Math.Abs(x),
                        "fact" or "factorial" => CalcularFactorial(x),
                        "deg" => x * (180.0 / System.Math.PI),
                        "rad" => x * (System.Math.PI / 180.0),
                        "floor" => System.Math.Floor(x),
                        "ceil" => System.Math.Ceiling(x),
                        "round" => System.Math.Round(x, MidpointRounding.AwayFromZero),
                        "sign" => System.Math.Sign(x),
                        _ => throw new MathSyntaxException($"Función no implementada: '{fn}'")
                    });
                }
                else if (argCount == 2)
                {
                    if (pila.Count < 2) throw new MathSyntaxException($"Función '{fn}' requiere 2 argumentos.");
                    var arg2 = pila.Pop();
                    var arg1 = pila.Pop();

                    pila.Push(fn switch
                    {
                        "root" or "nthroot" => arg1 == 0 ? throw new DivideByZeroException() : System.Math.Pow(arg2, 1.0 / arg1),
                        "log" when arg1 <= 0 || arg1 == 1 || arg2 <= 0 => throw new MathDomainException("Base o argumento de log inválido."),
                        "log" => System.Math.Log(arg2, arg1),
                        "round" => System.Math.Round(arg1, (int)arg2, MidpointRounding.AwayFromZero),
                        "atan2" => System.Math.Atan2(arg1, arg2),
                        _ => throw new MathSyntaxException($"Función de 2 argumentos desconocida: '{fn}'")
                    });
                }
                else
                {
                    throw new MathSyntaxException($"Cantidad de argumentos ({argCount}) inválida para '{fn}'.");
                }
            }
        }

        if (pila.Count != 1)
            throw new MathSyntaxException("Expresión malformada: operandos y operadores no coinciden.");

        return pila.Pop();
    }

    private static double CalcularFactorial(double n)
    {
        if (n < 0 || System.Math.Abs(n - System.Math.Round(n)) > 1e-9)
            throw new FactorialException($"El factorial solo está definido para enteros no negativos (recibido: {n}).");

        long entero = (long)System.Math.Round(n);
        if (entero > 170)
            throw new FactorialException("Desbordamiento numérico: n! supera el límite para n > 170.");

        double resultado = 1.0;
        for (long i = 2; i <= entero; i++)
            resultado *= i;

        return resultado;
    }

    // -------------------------------------------------------------
    // Conversión a Fracción Exacta Irreducible P/Q
    // -------------------------------------------------------------

    private static string? CalcularFraccionExacta(double valor, double tolerancia = 1e-8, long maxDenominador = 100000)
    {
        if (double.IsNaN(valor) || double.IsInfinity(valor) || System.Math.Abs(valor) > 1e9)
            return null;

        // Si ya es un número entero exacto, no mostrar fracción redundante
        if (System.Math.Abs(valor - System.Math.Round(valor)) < 1e-9)
            return null;

        bool negativo = valor < 0;
        double x = System.Math.Abs(valor);

        long h1 = 1, h2 = 0;
        long k1 = 0, k2 = 1;
        double b = x;

        do
        {
            long a = (long)System.Math.Floor(b);
            long h = a * h1 + h2;
            long k = a * k1 + k2;

            if (k > maxDenominador) break;

            h2 = h1; h1 = h;
            k2 = k1; k1 = k;

            double aprox = (double)h1 / k1;
            if (System.Math.Abs(x - aprox) < tolerancia)
            {
                long num = negativo ? -h1 : h1;
                long den = k1;

                if (den == 1) return null;

                // Formato simple P/Q
                if (System.Math.Abs(num) > den)
                {
                    long entero = num / den;
                    long resto = System.Math.Abs(num % den);
                    return $"{num}/{den} ({entero} {resto}/{den})";
                }

                return $"{num}/{den}";
            }

            if (System.Math.Abs(b - a) < 1e-12) break;
            b = 1.0 / (b - a);
        } while (k1 <= maxDenominador);

        return null;
    }
}

public sealed class MathSyntaxException(string message) : Exception(message);
public sealed class MathDomainException(string message) : Exception(message);
public sealed class FactorialException(string message) : Exception(message);
