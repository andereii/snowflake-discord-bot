# Comandos y Elementos Temporales para Eliminar en Release

Este documento registra los comandos o herramientas temporales creadas para pruebas de infraestructura y depuración que **DEBEN SER ELIMINADAS** antes del release público del bot.

---

## 1. Diagnóstico de Lavalink
- **Comandos:**
  - Slash: `/m status` / `/m estado` (`MusicModule.cs`)
  - Prefijo: `;lavalink` / `;lavalink-status` (`PrefixCommandService.cs`)
- **Métodos asociados:**
  - `MusicService.ConstruirEmbedEstadoLavalinkAsync`
  - Cliente HTTP `"LavalinkDiag"` en `Program.cs`
- **Motivo de eliminación:**
  - Es un comando de diagnóstico técnico interno (muestra IP/host, versión de JVM, memoria RAM, CPU load y latencia del nodo Lavalink) que no debe quedar expuesto al público en la versión final.
- **Acción requerida en Release:**
  - Borrar el método `StatusAsync` en `MusicModule.cs`.
  - Borrar `case "lavalink":` y `EjecutarLavalinkStatusAsync` en `PrefixCommandService.cs`.
  - Borrar `ConstruirEmbedEstadoLavalinkAsync` en `MusicService.cs`.
  - Borrar el cliente `"LavalinkDiag"` en `Program.cs`.
