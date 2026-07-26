namespace Seto90;

public sealed class TransactionLog
{
    readonly ProjectStore store;
    readonly List<string> undo = [];
    readonly List<string> redo = [];
    public TransactionLog(ProjectStore store) => this.store = store;
    public void BeforeChange(GameProject p) { undo.Add(store.Snapshot(p)); redo.Clear(); }
    /// <summary>Revierte al ultimo snapshot SIN dejar rastro en redo: es el rollback de una
    /// operacion fallida (validacion/guardado), no un undo del usuario — rehacer un estado
    /// invalido no debe ser posible.</summary>
    public bool Rollback(ref GameProject p) { if (undo.Count == 0) return false; var s = undo[^1]; undo.RemoveAt(undo.Count - 1); p = store.FromSnapshot(s); store.Save(p); return true; }
    public bool Undo(ref GameProject p) { if (undo.Count == 0) return false; redo.Add(store.Snapshot(p)); var s = undo[^1]; undo.RemoveAt(undo.Count - 1); p = store.FromSnapshot(s); store.Save(p); return true; }
    public bool Redo(ref GameProject p) { if (redo.Count == 0) return false; undo.Add(store.Snapshot(p)); var s = redo[^1]; redo.RemoveAt(redo.Count - 1); p = store.FromSnapshot(s); store.Save(p); return true; }
}
