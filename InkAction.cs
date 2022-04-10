using System.Collections.Generic;
using Windows.UI.Input.Inking;

namespace Notes
{
    internal class InkAction
    {
        public List<InkStroke> strokes;
        public bool erased;

        public InkAction(IReadOnlyList<InkStroke> strokes, bool erased)
        {
            this.strokes = new List<InkStroke>();
            this.strokes.AddRange(strokes);
            this.erased = erased;
        }
    }

    internal class InkActionStack
    {
        private List<InkAction> undo;
        private List<InkAction> redo;
        
        public int capacity;
        public int UndoCount => undo.Count;
        public int RedoCount => redo.Count;
        public bool allowModifications = true;

        public InkActionStack(int capacity)
        {
            this.capacity = capacity;
            undo = new List<InkAction>(capacity);
            redo = new List<InkAction>(capacity);
        }

        public void Undo(InkPresenter ink)
        {
            InkAction action = Pop(undo);
            if(action == null) { return; }

            if (action.erased)
            {
                List<InkStroke> newStrokes = new List<InkStroke>();
                foreach (InkStroke stroke in action.strokes)
                {
                    var strokeBuilder = new InkStrokeBuilder();
                    strokeBuilder.SetDefaultDrawingAttributes(stroke.DrawingAttributes);
                    System.Numerics.Matrix3x2 matr = stroke.PointTransform;
                    IReadOnlyList<InkPoint> inkPoints = stroke.GetInkPoints();
                    InkStroke stk = strokeBuilder.CreateStrokeFromInkPoints(inkPoints, matr);
                    newStrokes.Add(stk);
                    ink.StrokeContainer.AddStroke(stk);
                }

                action.strokes.AddRange(newStrokes);

                foreach (InkAction a in undo)
                {
                    if (a.strokes.Contains(action.strokes[0]))
                    {
                        a.strokes.Clear();
                        a.strokes.AddRange(newStrokes);
                    }
                }

                foreach (InkAction a in redo)
                {
                    if (a.strokes.Contains(action.strokes[0]))
                    {
                        a.strokes.Clear();
                        a.strokes.AddRange(newStrokes);
                    }
                }

                Push(redo, action);
            }
            else
            {
                foreach (InkStroke stroke in action.strokes)
                {
                    InkStroke s = ink.StrokeContainer.GetStrokeById(stroke.Id);
                    if (s != null) s.Selected = true;
                }

                ink.StrokeContainer.DeleteSelected();

                Push(redo, action);
            }
        }

        public void Redo(InkPresenter ink)
        {
            InkAction action = Pop(redo);
            if (action == null) { return; }

            if (action.erased)
            {
                foreach (InkStroke stroke in action.strokes)
                {
                    InkStroke s = ink.StrokeContainer.GetStrokeById(stroke.Id);
                    if (s != null) s.Selected = true;
                }
                ink.StrokeContainer.DeleteSelected();
                Push(undo, action);
            }
            else
            {
                List<InkStroke> newStrokes = new List<InkStroke>();
                foreach (InkStroke stroke in action.strokes)
                {
                    var strokeBuilder = new InkStrokeBuilder();
                    strokeBuilder.SetDefaultDrawingAttributes(stroke.DrawingAttributes);
                    System.Numerics.Matrix3x2 matr = stroke.PointTransform;
                    IReadOnlyList<InkPoint> inkPoints = stroke.GetInkPoints();
                    InkStroke stk = strokeBuilder.CreateStrokeFromInkPoints(inkPoints, matr);
                    newStrokes.Add(stk);
                    ink.StrokeContainer.AddStroke(stk);
                }

                action.strokes.AddRange(newStrokes);

                foreach (InkAction a in undo)
                {
                    if (a.strokes.Contains(action.strokes[0]))
                    {
                        a.strokes.Clear();
                        a.strokes.AddRange(newStrokes);
                    }
                }

                foreach(InkAction a in redo)
                {
                    if (a.strokes.Contains(action.strokes[0]))
                    {
                        a.strokes.Clear();
                        a.strokes.AddRange(newStrokes);
                    }
                }

                Push(undo, action);
            }
        }

        private InkAction Pop(List<InkAction> list)
        {
            if (!allowModifications) return null;

            InkAction a = list[list.Count - 1];
            list.RemoveAt(list.Count - 1);
            return a;
        }

        public void Push(IReadOnlyList<InkStroke> strokes, bool erased)
        {
            if (!allowModifications) return;

            if (undo.Count == capacity)
            {
                undo.RemoveAt(0);
            }
            
            InkAction action = new InkAction(strokes, erased);
            undo.Add(action);
        }

        private void Push(List<InkAction> list, InkAction action)
        {
            if (list.Count == capacity)
            {
                list.RemoveAt(0);
            }

            list.Add(action);
        }
    }
}