using System.Collections.ObjectModel;
using AvDashboard.Core.Contracts;
using AvDashboard.Core.Models;
using AvDashboard.Core.Video;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

namespace AvDashboard.Client.ViewModels;

/// <summary>
/// Vídeo (M4) y EDID Toolkit (M18).
///
/// La vista contesta a las dos preguntas que se hacen delante de un procesador LED que no
/// engancha: por qué conector físico sale cada señal y a qué refresco exacto, y qué dice
/// realmente el EDID del monitor que hay al otro lado.
/// </summary>
public sealed partial class VideoViewModel : PageViewModel
{
    public VideoViewModel(NodeManager nodes, IUiShell shell) : base(nodes, shell)
    {
    }

    public override string Title => "Vídeo";
    public override string Glyph => "\uE714";

    public ObservableCollection<DisplayOutputViewModel> Outputs { get; } = new();
    public ObservableCollection<EdidEntryViewModel> Entries { get; } = new();
    public ObservableCollection<EdidDifference> Differences { get; } = new();
    public ObservableCollection<EdidDrift> Drift { get; } = new();

    [ObservableProperty]
    private EdidEntryViewModel? _selected;

    [ObservableProperty]
    private string? _edidUnavailable;

    [ObservableProperty]
    private string _diffSummary = "Marca dos EDID de la lista y pulsa Comparar.";

    [ObservableProperty]
    private string? _dumpResult;

    /// <summary>Nombre con el que se guarda la referencia: «VX600 rack B», «Proyector sala 2».</summary>
    [ObservableProperty]
    private string? _referenceName;

    [ObservableProperty]
    private string _referenceSummary = "Sin referencias guardadas.";

    [ObservableProperty]
    private string? _panicResult;

    [ObservableProperty]
    private bool _isPanicBusy;

    protected override async Task LoadAsync()
    {
        ApiClient api = Nodes.Api();

        ApiResult<DisplayTopology> displays = await api.GetDisplaysAsync().ConfigureAwait(true);
        if (!Apply(displays, out DisplayTopology topology))
        {
            return;
        }

        Outputs.Clear();
        foreach (DisplayOutput output in topology.Outputs
                     .OrderByDescending(o => o.IsPrimary)
                     .ThenBy(o => o.DeviceName, StringComparer.CurrentCulture))
        {
            Outputs.Add(new DisplayOutputViewModel(output));
        }

        // Recordar qué había marcado antes del refresco: perder la selección cada cinco
        // segundos haría inutilizable el diff.
        HashSet<string> marked = Entries.Where(e => e.IsMarked).Select(e => e.Id).ToHashSet(StringComparer.Ordinal);
        string? selectedId = Selected?.Id;

        ApiResult<EdidListResponse> edid = await api.GetEdidAsync().ConfigureAwait(true);

        Entries.Clear();

        if (!edid.Success)
        {
            EdidUnavailable = edid.Error;
            Health = WorstOf(topology);
            return;
        }

        EdidListResponse list = edid.Value!;
        EdidUnavailable = list.UnavailableReason;

        // La seleccion se resuelve contra la lista nueva. Conservar el objeto viejo la
        // dejaria apuntando a una entrada que ya no esta en la coleccion.
        Selected = null;

        foreach (EdidEntry entry in list.Entries)
        {
            EdidEntryViewModel item = new(entry) { IsMarked = marked.Contains(entry.Id) };
            Entries.Add(item);

            if (entry.Id == selectedId)
            {
                Selected = item;
            }
        }

        Selected ??= Entries.FirstOrDefault(e => e.Connected) ?? Entries.FirstOrDefault();

        // La salud de la página combina las dos mitades: una salida perdida y un EDID con
        // el checksum roto son igual de graves y ninguna debe tapar a la otra.
        HealthState worst = WorstOf(topology);
        foreach (EdidEntryViewModel entry in Entries)
        {
            if (entry.Health > worst)
            {
                worst = entry.Health;
            }
        }

        await LoadReferencesAsync().ConfigureAwait(true);

        // Una salida cuyo EDID ya no coincide con su referencia es al menos un ambar: la
        // imagen se ve, pero lo que hay enchufado no es lo que habia.
        if (Drift.Count > 0 && worst < HealthState.Warning)
        {
            worst = HealthState.Warning;
        }

        Health = worst;
    }

    private async Task LoadReferencesAsync()
    {
        ApiResult<EdidReferenceListResponse> result =
            await Nodes.Api().GetEdidReferencesAsync().ConfigureAwait(true);

        Drift.Clear();

        if (!result.Success)
        {
            ReferenceSummary = result.Error ?? "No se han podido leer las referencias.";
            return;
        }

        EdidReferenceListResponse response = result.Value!;

        foreach (EdidDrift drift in response.Drift)
        {
            Drift.Add(drift);
        }

        ReferenceSummary = response.References.Count == 0
            ? "Sin referencias guardadas. Selecciona un EDID y guárdalo con un nombre para que el agente avise si cambia."
            : response.Drift.Count == 0
                ? $"{response.References.Count} referencias guardadas, todas coinciden."
                : $"{response.Drift.Count} de {response.References.Count} referencias ya no coinciden.";
    }

    /// <summary>
    /// Guarda el EDID seleccionado como referencia de su conector. A partir de ahí el agente
    /// avisa solo cuando esa salida presente otro: es la detección temprana de que alguien ha
    /// cambiado un splitter o un procesador.
    /// </summary>
    [RelayCommand]
    private async Task SaveReferenceAsync()
    {
        if (Selected is null)
        {
            return;
        }

        if (string.IsNullOrWhiteSpace(ReferenceName))
        {
            ReferenceSummary = "Ponle un nombre con el que la reconozcas dentro de seis meses.";
            return;
        }

        ApiResult<EdidReference> result = await Nodes.Api()
            .SaveEdidReferenceAsync(new SaveEdidReferenceRequest
            {
                Id = Selected.Id,
                Name = ReferenceName.Trim(),
            })
            .ConfigureAwait(true);

        if (!result.Success)
        {
            ReferenceSummary = result.Error ?? "No se ha podido guardar la referencia.";
            return;
        }

        ReferenceName = null;
        await LoadReferencesAsync().ConfigureAwait(true);
    }

    /// <summary>Salud de la topología por sí sola: sin salidas activas es un problema.</summary>
    private static HealthState WorstOf(DisplayTopology topology) =>
        topology.ActiveCount == 0 ? HealthState.Critical : HealthState.Ok;

    /// <summary>
    /// Vuelca el EDID seleccionado a .bin. Binario y no base64 porque es lo que comen CRU,
    /// AW EDID Editor y la carga de EDID de una matriz.
    /// </summary>
    [RelayCommand]
    private async Task DumpAsync()
    {
        if (Selected is null)
        {
            return;
        }

        ApiResult<EdidRawResponse> result = await Nodes.Api().GetEdidRawAsync(Selected.Id).ConfigureAwait(true);

        if (!result.Success)
        {
            DumpResult = result.Error;
            return;
        }

        EdidRawResponse raw = result.Value!;

        string? destino = await Shell
            .PickSaveFileAsync(raw.SuggestedFileName, ".bin", "EDID binario")
            .ConfigureAwait(true);

        if (destino is null)
        {
            return;
        }

        try
        {
            byte[] bytes = Convert.FromBase64String(raw.Base64);
            await File.WriteAllBytesAsync(destino, bytes).ConfigureAwait(true);
            DumpResult = $"{bytes.Length} bytes escritos en {destino}";
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or FormatException)
        {
            DumpResult = ex.Message;
        }
    }

    /// <summary>Compara los dos EDID marcados.</summary>
    [RelayCommand]
    private async Task DiffAsync()
    {
        EdidEntryViewModel[] marked = Entries.Where(e => e.IsMarked).Take(3).ToArray();

        if (marked.Length != 2)
        {
            DiffSummary = marked.Length < 2
                ? "Marca exactamente dos EDID para compararlos."
                : "Hay más de dos marcados. El diff compara dos.";
            Differences.Clear();
            return;
        }

        ApiResult<EdidDiffResponse> result = await Nodes.Api()
            .GetEdidDiffAsync(marked[0].Id, marked[1].Id)
            .ConfigureAwait(true);

        if (!result.Success)
        {
            DiffSummary = result.Error ?? "El diff ha fallado.";
            Differences.Clear();
            return;
        }

        EdidDiffResponse diff = result.Value!;

        Differences.Clear();
        foreach (EdidDifference difference in diff.Differences)
        {
            Differences.Add(difference);
        }

        int significant = diff.Differences.Count(d => d.Significant);

        DiffSummary = diff.Identical
            ? $"{diff.LeftLabel} y {diff.RightLabel} son idénticos."
            : $"{diff.Differences.Count} diferencias entre «{diff.LeftLabel}» y «{diff.RightLabel}», " +
              (significant == 0
                  ? "ninguna afecta a lo que se negocia en el cable."
                  : $"{significant} de ellas cambian lo que se negocia en el cable.");
    }

    /// <summary>
    /// Reinicia el controlador gráfico (M15).
    ///
    /// Vive en esta pestaña y no en la barra porque es aquí donde miras cuando la pantalla
    /// se ha quedado congelada. Deja las salidas en negro un par de segundos, así que no se
    /// dispara sin confirmar, y el diálogo lo dice con esas palabras.
    /// </summary>
    [RelayCommand]
    private async Task RestartGraphicsDriverAsync()
    {
        bool confirmado = await Shell.ConfirmAsync(
            "Reiniciar el controlador gráfico",
            "Las salidas de vídeo se quedarán en negro uno o dos segundos y Windows puede " +
            "recolocar las ventanas.",
            "Reiniciar").ConfigureAwait(true);

        if (!confirmado)
        {
            return;
        }

        IsPanicBusy = true;

        try
        {
            ApiResult<GraphicsRestartResponse> result =
                await Nodes.Api().RestartGraphicsDriverAsync().ConfigureAwait(true);

            PanicResult = result.Success
                ? result.Value!.Message
                : result.Error ?? "El reinicio del controlador gráfico ha fallado.";
        }
        finally
        {
            IsPanicBusy = false;
        }

        await RefreshAsync().ConfigureAwait(true);
    }

    [RelayCommand]
    private void ClearMarks()
    {
        foreach (EdidEntryViewModel entry in Entries)
        {
            entry.IsMarked = false;
        }

        Differences.Clear();
        DiffSummary = "Marca dos EDID de la lista y pulsa Comparar.";
    }
}

/// <summary>Una salida activa, con los campos ya formateados para la vista.</summary>
public sealed class DisplayOutputViewModel
{
    public DisplayOutputViewModel(DisplayOutput output)
    {
        Output = output;
    }

    public DisplayOutput Output { get; }

    public string DeviceName => Output.DeviceName;
    /// <summary>
    /// El nombre que se lee. Sale de Core para que el panel y esta pagina digan lo mismo:
    /// antes el panel ensenaba el nombre de dispositivo en crudo y aqui «monitor sin
    /// nombre», que son dos respuestas distintas a la misma pregunta.
    /// </summary>
    public string MonitorName => Output.DisplayLabel;
    public string Connector => Output.PhysicalConnector ?? "conector desconocido";
    public bool IsPrimary => Output.IsPrimary;

    public string Mode => $"{Output.Width}×{Output.Height}";

    /// <summary>
    /// El refresco escrito de las dos formas: el decimal para leerlo de un vistazo y la
    /// fracción exacta debajo, porque 60000/1001 y 60/1 se ven igual redondeados y son
    /// justo la diferencia que acumula deriva a lo largo de un show.
    /// </summary>
    public string Refresh => Output.RefreshDenominator > 0
        ? $"{Output.RefreshHz:0.###} Hz  ({Output.RefreshNumerator}/{Output.RefreshDenominator})"
        : $"{Output.RefreshHz:0.###} Hz";

    public string Color
    {
        get
        {
            string depth = Output.BitsPerColorChannel is int bits ? $"{bits} bit/canal" : "profundidad desconocida";

            return Output.HdrEnabled
                ? $"HDR activo · {depth}"
                : Output.HdrSupported ? $"HDR soportado, apagado · {depth}" : depth;
        }
    }

    public string Edid => Output.EdidAvailable
        ? $"EDID {Output.EdidHash}" +
          (Output.EdidSerial is { Length: > 0 } serial ? $" · s/n {serial}" : string.Empty)
        : "sin EDID legible";
}

/// <summary>
/// Una entrada de la biblioteca de EDID. Envuelve el contrato para añadirle la marca de
/// selección del diff, que es estado de la vista y no del agente.
/// </summary>
public sealed partial class EdidEntryViewModel : ObservableObject
{
    public EdidEntryViewModel(EdidEntry entry)
    {
        Entry = entry;
    }

    public EdidEntry Entry { get; }

    [ObservableProperty]
    private bool _isMarked;

    public string Id => Entry.Id;
    public string Label => Entry.Label;
    public bool Connected => Entry.Connected;
    public HealthState Health => Entry.Health;
    public string Fingerprint => Entry.Fingerprint;

    public string Where => Entry.Connected
        ? $"conectado · {Entry.DeviceName}"
        : "recordado por Windows";

    public string Summary
    {
        get
        {
            Edid? parsed = Entry.Parsed;

            if (parsed is null)
            {
                return $"{Entry.ByteCount} bytes que no se han podido decodificar";
            }

            string preferred = parsed.PreferredTiming is { } timing
                ? $"{timing.HorizontalActive}×{timing.VerticalActive}@{timing.RefreshHz:0.###} Hz"
                : "sin modo preferido";

            string interfaceName = parsed.IsDigitalInput
                ? parsed.DigitalInterface ?? "digital"
                : "analógica";

            return $"{preferred} · {interfaceName} · EDID {parsed.Version}.{parsed.Revision} · {Entry.ByteCount} bytes";
        }
    }

    public IReadOnlyList<EdidFinding> Findings => Entry.Findings;

    /// <summary>Timings detallados del EDID, que es donde se mira cuando algo no negocia.</summary>
    public IReadOnlyList<DetailedTimingRow> DetailedTimings =>
        (Entry.Parsed?.DetailedTimings ?? Array.Empty<DetailedTiming>())
        .Select(t => new DetailedTimingRow(t))
        .ToList();

    public IReadOnlyList<ShortAudioDescriptor> AudioDescriptors =>
        Entry.Parsed?.Cta?.AudioDescriptors ?? Array.Empty<ShortAudioDescriptor>();

    public string RangeLimits => Entry.Parsed?.RangeLimits?.ToString() ?? "sin límites declarados";

    public string Hdr => Entry.Parsed?.Cta?.Hdr is { } hdr && hdr.SupportedEotf.Count > 0
        ? string.Join(", ", hdr.SupportedEotf)
        : "sin metadatos HDR";
}

/// <summary>
/// Un timing detallado, ya escrito.
///
/// El reloj de pixel y los totales son numeros que sin unidad ni etiqueta no se leen, y en
/// WinUI los enlaces no tienen <c>StringFormat</c> con el que ponersela desde el XAML.
/// </summary>
public sealed class DetailedTimingRow
{
    public DetailedTimingRow(DetailedTiming source)
    {
        Timing = source;
    }

    public DetailedTiming Timing { get; }

    /// <summary>El modo tal cual lo escribe el propio timing: «1920×1080@60 Hz».</summary>
    public string ModeText => Timing.ToString() ?? string.Empty;

    public string PixelClockText => $"{Timing.PixelClockKhz:N0} kHz";

    /// <summary>
    /// Totales con blanking incluido. Son los que hay que comparar contra lo que declara un
    /// procesador LED cuando el modo entra pero la imagen sale corrida.
    /// </summary>
    public string TotalsText => $"total {Timing.HorizontalTotal} × {Timing.VerticalTotal}";
}
