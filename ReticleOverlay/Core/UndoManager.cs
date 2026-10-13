using System;
using System.Collections.Generic;

namespace ReticleOverlay
{
    /// <summary>
    /// Snapshot-based undo/redo for reticle settings.
    /// Uses LinkedList for O(1) push and tail-trim (max 50 steps).
    /// Each entry carries a human-readable description shown in undo/redo tooltips (#17).
    /// </summary>
    public sealed class UndoManager
    {
        private const int MaxDepth = 50;

        private record Entry(ReticleSnapshot Snapshot, string Description);

        private readonly LinkedList<Entry> _undo = new LinkedList<Entry>();
        private readonly LinkedList<Entry> _redo = new LinkedList<Entry>();

        public bool CanUndo => _undo.Count > 0;
        public bool CanRedo => _redo.Count > 0;

        public string UndoDescription => _undo.First?.Value.Description ?? "";
        public string RedoDescription => _redo.First?.Value.Description ?? "";

        public event EventHandler StateChanged;

        public void Push(AppPrefs p, string description = "")
        {
            _undo.AddFirst(new Entry(ReticleSnapshot.Capture(p), description));
            _redo.Clear();
            if (_undo.Count > MaxDepth) _undo.RemoveLast();
            StateChanged?.Invoke(this, EventArgs.Empty);
        }

        public bool Undo(AppPrefs p)
        {
            if (!CanUndo) return false;
            _redo.AddFirst(new Entry(ReticleSnapshot.Capture(p), _undo.First!.Value.Description));
            _undo.First!.Value.Snapshot.Apply(p);
            _undo.RemoveFirst();
            StateChanged?.Invoke(this, EventArgs.Empty);
            return true;
        }

        public bool Redo(AppPrefs p)
        {
            if (!CanRedo) return false;
            _undo.AddFirst(new Entry(ReticleSnapshot.Capture(p), _redo.First!.Value.Description));
            _redo.First!.Value.Snapshot.Apply(p);
            _redo.RemoveFirst();
            StateChanged?.Invoke(this, EventArgs.Empty);
            return true;
        }

        public void Clear()
        {
            _undo.Clear();
            _redo.Clear();
            StateChanged?.Invoke(this, EventArgs.Empty);
        }

        public void ClearUndo()
        {
            _undo.Clear();
            StateChanged?.Invoke(this, EventArgs.Empty);
        }
    }

    internal sealed class ReticleSnapshot
    {
        public string ReticleType      { get; }
        public string CustomImagePath  { get; }
        public double ReticleSize      { get; }
        public double ReticleThickness { get; }
        public double CentreGap        { get; }
        public double Rotation         { get; }
        public string ReticleColor     { get; }
        public string ReticleColor2    { get; }
        public bool   UseColor2        { get; }
        public string OutlineColor     { get; }
        public bool   ShowOutline      { get; }
        public double Opacity          { get; }
        public double OffsetX          { get; }
        public double OffsetY          { get; }
        public bool   Layer2Enabled    { get; }
        public string Layer2Type       { get; }
        public double Layer2Size       { get; }
        public double Layer2Thickness  { get; }
        public double Layer2CentreGap  { get; }
        public double Layer2Rotation   { get; }
        public string Layer2Color      { get; }
        public double Layer2Opacity    { get; }

        private ReticleSnapshot(AppPrefs p)
        {
            ReticleType=p.ReticleType; CustomImagePath=p.CustomImagePath;
            ReticleSize=p.ReticleSize; ReticleThickness=p.ReticleThickness;
            CentreGap=p.CentreGap; Rotation=p.Rotation;
            ReticleColor=p.ReticleColor; ReticleColor2=p.ReticleColor2;
            UseColor2=p.UseColor2; OutlineColor=p.OutlineColor;
            ShowOutline=p.ShowOutline; Opacity=p.Opacity;
            OffsetX=p.OffsetX; OffsetY=p.OffsetY;
            Layer2Enabled=p.Layer2Enabled; Layer2Type=p.Layer2Type;
            Layer2Size=p.Layer2Size; Layer2Thickness=p.Layer2Thickness;
            Layer2CentreGap=p.Layer2CentreGap; Layer2Rotation=p.Layer2Rotation;
            Layer2Color=p.Layer2Color; Layer2Opacity=p.Layer2Opacity;
        }

        public static ReticleSnapshot Capture(AppPrefs p) => new ReticleSnapshot(p);

        public void Apply(AppPrefs p)
        {
            p.ReticleType=ReticleType; p.CustomImagePath=CustomImagePath;
            p.ReticleSize=ReticleSize; p.ReticleThickness=ReticleThickness;
            p.CentreGap=CentreGap; p.Rotation=Rotation;
            p.ReticleColor=ReticleColor; p.ReticleColor2=ReticleColor2;
            p.UseColor2=UseColor2; p.OutlineColor=OutlineColor;
            p.ShowOutline=ShowOutline; p.Opacity=Opacity;
            p.OffsetX=OffsetX; p.OffsetY=OffsetY;
            p.Layer2Enabled=Layer2Enabled; p.Layer2Type=Layer2Type;
            p.Layer2Size=Layer2Size; p.Layer2Thickness=Layer2Thickness;
            p.Layer2CentreGap=Layer2CentreGap; p.Layer2Rotation=Layer2Rotation;
            p.Layer2Color=Layer2Color; p.Layer2Opacity=Layer2Opacity;
        }
    }
}
