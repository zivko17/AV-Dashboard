using AvDashboard.Core.Interfaces;
using AvDashboard.Core.Models;
using AvDashboard.Core.Profiles;
using Xunit;

namespace AvDashboard.Core.Tests;

/// <summary>
/// «Ningún cambio sin snapshot previo persistido» es un requisito no funcional. Este es el
/// sitio donde se cumple o no se cumple.
/// </summary>
public class SnapshotEngineTests
{
    [Fact]
    public async Task Captura_todos_los_modulos_y_los_persiste()
    {
        FakeRepository repository = new();
        SnapshotEngine engine = new(
            new[] { Module("M1", "plan=equilibrado"), Module("M5", "ip=dhcp") },
            repository);

        string id = await engine.CaptureAsync("nodo-1", "manual");

        Snapshot saved = Assert.Single(repository.Saved);
        Assert.Equal(id, saved.Id);
        Assert.Equal("nodo-1", saved.NodeId);
        Assert.Equal("manual", saved.Trigger);
        Assert.Equal("plan=equilibrado", saved.Payload["M1"]);
        Assert.Equal("ip=dhcp", saved.Payload["M5"]);
        Assert.Null(saved.Notes);
    }

    [Fact]
    public async Task Un_modulo_que_no_sabe_capturar_no_aborta_el_snapshot_entero()
    {
        // Un snapshot parcial que dice qué le falta es mucho más útil que ningún snapshot:
        // sin snapshot no se puede aplicar un perfil, y ese es el peor resultado.
        FakeRepository repository = new();
        SnapshotEngine engine = new(
            new[] { Module("M1", "plan=equilibrado"), FailingModule("M4", "no hay NVAPI") },
            repository);

        await engine.CaptureAsync("nodo-1", "pre-apply");

        Snapshot saved = Assert.Single(repository.Saved);
        Assert.True(saved.Payload.ContainsKey("M1"));
        Assert.False(saved.Payload.ContainsKey("M4"));
        Assert.Contains("M4", saved.Notes);
        Assert.Contains("no hay NVAPI", saved.Notes);
    }

    [Fact]
    public async Task Restaura_cada_modulo_con_lo_que_se_capturo()
    {
        FakeRepository repository = new();
        RecordingModule m1 = new("M1");
        RecordingModule m5 = new("M5");

        SnapshotEngine engine = new(new ISnapshotable[] { m1, m5 }, repository);
        string id = await engine.CaptureAsync("nodo-1", "manual");

        RestoreResult result = await engine.RestoreAsync(id);

        Assert.True(result.Success);
        Assert.Equal("estado-de-M1", m1.RestoredWith);
        Assert.Equal("estado-de-M5", m5.RestoredWith);
    }

    [Fact]
    public async Task Un_modulo_que_falla_al_restaurar_no_detiene_a_los_demas()
    {
        // Un rollback que se para a la mitad deja la máquina en un estado que no es ni el
        // de antes ni el de después: el peor de los tres.
        FakeRepository repository = new();
        RecordingModule good = new("M5");

        SnapshotEngine engine = new(
            new ISnapshotable[] { FailingRestoreModule("M1"), good },
            repository);

        string id = await engine.CaptureAsync("nodo-1", "manual");
        RestoreResult result = await engine.RestoreAsync(id);

        Assert.False(result.Success);
        Assert.Equal("estado-de-M5", good.RestoredWith);
        Assert.Contains("M1", result.Message);
    }

    [Fact]
    public async Task Restaurar_un_snapshot_que_no_existe_lo_dice_en_vez_de_reventar()
    {
        SnapshotEngine engine = new(Array.Empty<ISnapshotable>(), new FakeRepository());

        RestoreResult result = await engine.RestoreAsync("no-existe");

        Assert.False(result.Success);
        Assert.Contains("no-existe", result.Message);
    }

    [Fact]
    public async Task Un_modulo_nuevo_ausente_en_un_snapshot_antiguo_no_cuenta_como_fallo()
    {
        FakeRepository repository = new();
        SnapshotEngine capture = new(new[] { Module("M1", "estado") }, repository);
        string id = await capture.CaptureAsync("nodo-1", "manual");

        // El agente se actualiza y aparece un módulo que no existía cuando se capturó.
        RecordingModule nuevo = new("M14");
        SnapshotEngine restore = new(new ISnapshotable[] { Module("M1", "estado"), nuevo }, repository);

        RestoreResult result = await restore.RestoreAsync(id);

        Assert.True(result.Success);
        Assert.Null(nuevo.RestoredWith);
    }

    // --- Dobles de prueba ---

    private static ISnapshotable Module(string id, string state) => new RecordingModule(id, state);

    private static ISnapshotable FailingModule(string id, string reason) =>
        new ThrowingModule(id, reason, onCapture: true);

    private static ISnapshotable FailingRestoreModule(string id) =>
        new ThrowingModule(id, "no se puede restaurar", onCapture: false);

    private sealed class RecordingModule : ISnapshotable
    {
        private readonly string _state;

        public RecordingModule(string moduleId, string? state = null)
        {
            ModuleId = moduleId;
            _state = state ?? $"estado-de-{moduleId}";
        }

        public string ModuleId { get; }
        public string? RestoredWith { get; private set; }

        public Task<string> CaptureAsync(CancellationToken cancellationToken = default) =>
            Task.FromResult(_state);

        public Task<bool> RestoreAsync(string payload, CancellationToken cancellationToken = default)
        {
            RestoredWith = payload;
            return Task.FromResult(true);
        }
    }

    private sealed class ThrowingModule : ISnapshotable
    {
        private readonly string _reason;
        private readonly bool _onCapture;

        public ThrowingModule(string moduleId, string reason, bool onCapture)
        {
            ModuleId = moduleId;
            _reason = reason;
            _onCapture = onCapture;
        }

        public string ModuleId { get; }

        public Task<string> CaptureAsync(CancellationToken cancellationToken = default) =>
            _onCapture
                ? throw new InvalidOperationException(_reason)
                : Task.FromResult($"estado-de-{ModuleId}");

        public Task<bool> RestoreAsync(string payload, CancellationToken cancellationToken = default) =>
            _onCapture ? Task.FromResult(true) : throw new InvalidOperationException(_reason);
    }

    private sealed class FakeRepository : ISnapshotRepository
    {
        public List<Snapshot> Saved { get; } = new();

        public Task SaveAsync(Snapshot snapshot, CancellationToken cancellationToken = default)
        {
            Saved.Add(snapshot);
            return Task.CompletedTask;
        }

        public Task<Snapshot?> LoadAsync(string snapshotId, CancellationToken cancellationToken = default) =>
            Task.FromResult(Saved.FirstOrDefault(s => s.Id == snapshotId));

        public Task<IReadOnlyList<Snapshot>> ListAsync(
            string nodeId, int limit = 50, CancellationToken cancellationToken = default) =>
            Task.FromResult<IReadOnlyList<Snapshot>>(
                Saved.Where(s => s.NodeId == nodeId).Take(limit).ToList());
    }
}
