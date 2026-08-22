# Audio no documentado

Todo lo de esta carpeta usa APIs de Windows **sin documentación pública**:
`IPolicyConfig` (cambiar el dispositivo por defecto) e `IAudioPolicyConfig` (ruteo por
aplicación). Son las mismas que usa EarTrumpet, y funcionan, pero Microsoft no las
soporta y sus IID han cambiado entre builds.

Reglas de esta carpeta, sin excepciones:

1. **Nada de aquí se llama directamente desde los colectores.** Se accede a través de
   `UndocumentedAudio`, que detecta la build y devuelve `null` cuando no hay una
   variante conocida.
2. **Ningún fallo de aquí puede tirar el agente.** Todo `HRESULT` se comprueba, toda
   excepción de COM se captura, y el resultado es una degradación visible en la UI,
   no un crash en una máquina que está produciendo.
3. **Si una build nueva rompe una interfaz, el módulo se apaga solo** y la consola
   dice «no disponible en esta versión de Windows» en lugar de mentir.

Lo que se pierde cuando esto no está disponible: cambiar el dispositivo por defecto y
rutear una aplicación a otra salida desde la app. Todo lo demás de M3 —enumeración,
formatos, mismatch de sample rate, medidores, ASIO— usa API documentada y sigue
funcionando.
