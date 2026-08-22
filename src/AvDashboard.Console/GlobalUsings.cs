// Usings globales de la consola, declarados a mano y no por <ImplicitUsings>.
//
// Motivo: para compilar el XAML, el SDK de WPF genera un proyecto temporal
// (AvDashboard.Console_xxxxx_wpftmp.csproj) que no arrastra los usings implícitos del
// proyecto original. El resultado es que el proyecto compila en un IDE y falla en la
// compilación de marcado con un CS0103 por `Path`, `File` o `HttpClient`, que es
// exactamente el tipo de error que cuesta media hora entender.
//
// Un fichero de código sí entra en el proyecto temporal, así que aquí hay una sola
// fuente de verdad que vale para los dos.
global using System;
global using System.Collections.Generic;
global using System.IO;
global using System.Linq;
global using System.Net.Http;
global using System.Threading;
global using System.Threading.Tasks;
