# Instrucciones para Copilot

Panel de control para rigs de AV en directo sobre Windows: un agente por máquina y una
consola que lo mira. .NET 8, WPF hoy y WinUI 3 en curso.

El contexto completo está en [`CLAUDE.md`](../CLAUDE.md); lo esencial:

**La regla que no se rompe.** El agente nunca ejecuta comandos arbitrarios. No propongas un
endpoint que lance PowerShell, `cmd`, `pnputil` ni ningún binario. Toda escritura pasa por
un actuador compilado, y el `switch` de `SettingsWriter.Apply` es la lista blanca.

**Castellano** en interfaz, comentarios, documentación y mensajes de commit. Los
identificadores, en inglés.

**Los comentarios explican por qué, no qué.** Si repite la línea de abajo, sobra.

**Core y Client son `net8.0` a secas**, sin nada de Windows, y el CI de Linux los compila
para que siga siendo verdad. Si algo se puede escribir sin tocar Windows, va ahí.

**Toda escritura deja rastro en el Timeline** con el valor anterior. No hay excepción.

**`docs/grafo.md` se genera**; no lo edites a mano.
