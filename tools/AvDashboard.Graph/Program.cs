using System.Text;
using System.Text.RegularExpressions;
using AvDashboard.Core.Contracts;
using AvDashboard.Core.Profiles;
using AvDashboard.Core.Settings;
using AvDashboard.Core.Taxonomy;

namespace AvDashboard.Graph;

/// <summary>
/// Regenera <c>docs/grafo.md</c>: el mapa del proyecto en Mermaid, sacado del código y no
/// de la memoria de nadie.
///
/// Un diagrama dibujado a mano miente a las dos semanas. Este se regenera en cada commit
/// desde las mismas fuentes que compilan, así que o está bien o el CI se pone rojo.
///
/// Dos reglas que no se pueden romper sin cargarse la idea:
///
/// El fichero no lleva fecha ni hash de commit. Si los llevara, cambiaría en cada
/// ejecución, el gancho lo volvería a añadir y el CI nunca podría comprobar si está al día.
/// La salida tiene que ser función pura de las fuentes.
///
/// Y lo que se puede leer del ensamblado se lee del ensamblado. Solo se recurre a las
/// expresiones regulares para lo que vive en proyectos que este generador no puede
/// referenciar —el registro de módulos está en el agente, que es net8.0-windows— y para
/// los ficheros de proyecto, que son XML de MSBuild.
/// </summary>
internal static class Program
{
    private static int Main(string[] args)
    {
        string root = args.Length > 0 ? args[0] : FindRepositoryRoot();
        string output = Path.Combine(root, "docs", "grafo.md");

        string generated = Build(root);

        // --check: no escribe, solo dice si lo que hay en el repositorio coincide. Es lo
        // que corre el CI para que nadie pueda commitear con el grafo desfasado.
        bool checkOnly = args.Contains("--check", StringComparer.Ordinal);

        string existing = File.Exists(output) ? File.ReadAllText(output) : string.Empty;

        if (Normalize(existing) == Normalize(generated))
        {
            Console.WriteLine($"docs/grafo.md está al día.");
            return 0;
        }

        if (checkOnly)
        {
            Console.Error.WriteLine(
                "docs/grafo.md no coincide con el código. Ejecuta `dotnet run --project " +
                "tools/AvDashboard.Graph` y añade el resultado al commit.");

            // Se imprime lo que deberia haber. Cuesta unas lineas de registro y ahorra
            // tener que reproducir el fallo en local para saber que ha cambiado.
            Console.Error.WriteLine();
            Console.Error.WriteLine("----- contenido esperado -----");
            Console.Error.WriteLine(generated);
            Console.Error.WriteLine("----- fin -----");

            return 1;
        }

        Directory.CreateDirectory(Path.GetDirectoryName(output)!);
        File.WriteAllText(output, generated);

        Console.WriteLine($"docs/grafo.md regenerado.");
        return 0;
    }

    /// <summary>Los finales de línea no cuentan: el gancho corre en Windows y el CI en Linux.</summary>
    private static string Normalize(string text) =>
        text.Replace("\r\n", "\n", StringComparison.Ordinal).TrimEnd();

    private static string FindRepositoryRoot()
    {
        DirectoryInfo? directory = new(AppContext.BaseDirectory);

        while (directory is not null && !File.Exists(Path.Combine(directory.FullName, "AvDashboard.sln")))
        {
            directory = directory.Parent;
        }

        return directory?.FullName
               ?? throw new InvalidOperationException("No se ha encontrado AvDashboard.sln.");
    }

    private static string Build(string root)
    {
        StringBuilder page = new();

        page.AppendLine("# Mapa del proyecto");
        page.AppendLine();
        page.AppendLine("> Generado por `tools/AvDashboard.Graph` desde el propio código. **No lo edites a");
        page.AppendLine("> mano**: el gancho de commit lo regenera y el CI comprueba que coincide.");
        page.AppendLine(">");
        page.AppendLine("> Un diagrama dibujado a mano miente a las dos semanas. Este no puede.");
        page.AppendLine();

        AppendProjectGraph(page, root);
        AppendModuleGraph(page, root);
        AppendSettingsTable(page);
        AppendSurface(page);

        return page.ToString();
    }

    // ------------------------------------------------------------------- Proyectos

    private static void AppendProjectGraph(StringBuilder page, string root)
    {
        List<(string Name, string Framework, List<string> References)> projects = new();

        foreach (string file in Directory
                     .EnumerateFiles(root, "*.csproj", SearchOption.AllDirectories)
                     .Where(f => !f.Contains($"{Path.DirectorySeparatorChar}bin{Path.DirectorySeparatorChar}",
                                     StringComparison.Ordinal) &&
                                 !f.Contains($"{Path.DirectorySeparatorChar}obj{Path.DirectorySeparatorChar}",
                                     StringComparison.Ordinal))
                     .OrderBy(f => f, StringComparer.Ordinal))
        {
            string text = File.ReadAllText(file);
            string name = Path.GetFileNameWithoutExtension(file);

            string framework = Match(text, @"<TargetFramework>([^<]+)</TargetFramework>") ?? "?";

            List<string> references = Regex
                .Matches(text, @"<ProjectReference\s+Include=""([^""]+)""")
                .Select(m => Path.GetFileNameWithoutExtension(
                    m.Groups[1].Value.Replace('\\', Path.DirectorySeparatorChar)))
                .ToList();

            projects.Add((name, framework, references));
        }

        page.AppendLine("## Proyectos y dependencias");
        page.AppendLine();
        page.AppendLine("```mermaid");
        page.AppendLine("graph TD");

        foreach ((string name, string framework, _) in projects)
        {
            // El marco importa: lo que es net8.0 a secas es lo que el CI puede probar en
            // Linux, y por tanto donde conviene que viva la logica.
            string shape = framework.Contains("windows", StringComparison.Ordinal)
                ? $"{Id(name)}[\"{name}<br/><small>{framework}</small>\"]"
                : $"{Id(name)}([\"{name}<br/><small>{framework}</small>\"])";

            page.AppendLine($"    {shape}");
        }

        page.AppendLine();

        foreach ((string name, _, List<string> references) in projects)
        {
            foreach (string reference in references)
            {
                page.AppendLine($"    {Id(name)} --> {Id(reference)}");
            }
        }

        page.AppendLine("```");
        page.AppendLine();
        page.AppendLine("Los proyectos redondeados son multiplataforma y el CI los prueba en Linux. Los");
        page.AppendLine("rectangulares dependen de Windows: COM, P/Invoke y WPF.");
        page.AppendLine();
    }

    // --------------------------------------------------------------------- Módulos

    private static void AppendModuleGraph(StringBuilder page, string root)
    {
        string registry = Path.Combine(root, "src", "AvDashboard.Agent", "Modules", "ModuleRegistry.cs");

        if (!File.Exists(registry))
        {
            return;
        }

        // El registro vive en el agente, que es net8.0-windows y no se puede referenciar
        // desde aqui. De ahi la expresion regular en vez de una lectura del ensamblado.
        MatchCollection matches = Regex.Matches(
            File.ReadAllText(registry),
            @"Add\(""(?<id>M[0-9A-Za-z-]+)"",\s*""(?<name>[^""]+)"",\s*ModuleAvailability\.(?<state>\w+)");

        if (matches.Count == 0)
        {
            return;
        }

        page.AppendLine("## Módulos");
        page.AppendLine();
        page.AppendLine("```mermaid");
        page.AppendLine("graph LR");

        Dictionary<string, List<string>> byState = new(StringComparer.Ordinal);

        foreach (Match match in matches)
        {
            string state = match.Groups["state"].Value;
            string label = $"{match.Groups["id"].Value} {match.Groups["name"].Value}";

            if (!byState.TryGetValue(state, out List<string>? bucket))
            {
                bucket = new List<string>();
                byState[state] = bucket;
            }

            bucket.Add(label);
        }

        foreach ((string state, List<string> modules) in byState.OrderBy(p => Rank(p.Key)))
        {
            page.AppendLine($"    subgraph {Id(state)}[\"{DescribeState(state)} · {modules.Count}\"]");
            page.AppendLine("        direction TB");

            foreach (string module in modules)
            {
                page.AppendLine($"        {Id(module)}[\"{module}\"]");
            }

            page.AppendLine("    end");
        }

        page.AppendLine("```");
        page.AppendLine();
    }

    private static int Rank(string state) => state switch
    {
        "Available" => 0,
        "Degraded" => 1,
        "MissingDependency" => 2,
        "NotImplemented" => 3,
        _ => 4,
    };

    private static string DescribeState(string state) => state switch
    {
        "Available" => "Funciona",
        "Degraded" => "Funciona a medias",
        "MissingDependency" => "Falta una dependencia",
        "NotImplemented" => "Todavía no",
        "Disabled" => "Desactivado en este nodo",
        _ => state,
    };

    // -------------------------------------------------------- Catálogo de ajustes

    private static void AppendSettingsTable(StringBuilder page)
    {
        page.AppendLine("## Catálogo de ajustes (M1)");
        page.AppendLine();
        page.AppendLine("| Grupo | Ajustes | Con objetivo | Impacto alto | Fuera de los perfiles |");
        page.AppendLine("|---|---:|---:|---:|---:|");

        foreach (SettingGroup group in SettingsCatalog.Groups)
        {
            SettingDefinition[] rows = SettingsCatalog.All.Where(d => d.Group == group).ToArray();

            if (rows.Length == 0)
            {
                continue;
            }

            page.AppendLine(
                $"| {SettingsCatalog.DescribeGroup(group)} " +
                $"| {rows.Length} " +
                $"| {rows.Count(r => r.HasTarget)} " +
                $"| {rows.Count(r => r.Impact == SettingImpact.High)} " +
                $"| {rows.Count(r => r.ExcludedFromProfiles)} |");
        }

        page.AppendLine(
            $"| **Total** | **{SettingsCatalog.All.Count}** " +
            $"| **{SettingsCatalog.All.Count(r => r.HasTarget)}** " +
            $"| **{SettingsCatalog.All.Count(r => r.Impact == SettingImpact.High)}** " +
            $"| **{SettingsCatalog.All.Count(r => r.ExcludedFromProfiles)}** |");

        page.AppendLine();
        page.AppendLine("«Fuera de los perfiles» son los que nunca entran en un cambio automático porque");
        page.AppendLine("tocarlos reduce la seguridad de la máquina de forma real.");
        page.AppendLine();
    }

    // ------------------------------------------------------- Superficie expuesta

    private static void AppendSurface(StringBuilder page)
    {
        page.AppendLine("## Superficie");
        page.AppendLine();
        page.AppendLine($"- **{CommandCatalog.All.Count} comandos** de CLI y paleta.");
        page.AppendLine($"- **{ActionIds.All.Count} actuadores** declarados, " +
                        $"de los cuales {ActionIds.Disruptive.Count} pueden interrumpir una salida en directo.");
        page.AppendLine();

        page.AppendLine("### Comandos por sector");
        page.AppendLine();
        page.AppendLine("| Sector | Comandos |");
        page.AppendLine("|---|---|");

        foreach (IGrouping<Sector, CommandDescriptor> group in CommandCatalog.All
                     .GroupBy(c => c.Sector)
                     .OrderBy(g => g.Key))
        {
            string names = string.Join(", ", group
                .Select(c => $"`{c.Name}`")
                .OrderBy(n => n, StringComparer.Ordinal));

            page.AppendLine($"| {group.Key} | {names} |");
        }

        page.AppendLine();
    }

    // ----------------------------------------------------------------- Utilidades

    /// <summary>
    /// Identificador de nodo válido para Mermaid. Un nombre con puntos o espacios rompe el
    /// diagrama entero, y el error que da no dice cuál de los cuarenta nodos ha sido.
    /// </summary>
    private static string Id(string name)
    {
        StringBuilder id = new(name.Length);

        foreach (char c in name)
        {
            id.Append(char.IsLetterOrDigit(c) ? c : '_');
        }

        return id.ToString();
    }

    private static string? Match(string text, string pattern)
    {
        Match match = Regex.Match(text, pattern);
        return match.Success ? match.Groups[1].Value : null;
    }
}
