using System;
using System.Collections.Generic;
using Windows.UI.Xaml;
using Windows.UI.Xaml.Controls;
using Windows.UI.Input.Inking;
using Windows.Storage.Streams;
using Windows.Storage.AccessCache;
using Windows.Storage;
using System.Threading.Tasks;
using System.Diagnostics;
using Windows.UI.Xaml.Shapes;
using Windows.UI.Xaml.Media;
using System.IO;
using Windows.UI;
using Windows.ApplicationModel.Core;
using Windows.UI.ViewManagement;
using Windows.Foundation;
using Windows.UI.Core;
using System.Linq;
using Windows.UI.Xaml.Input;

namespace Notes
{
    public sealed partial class MainPage : Page
    {
        private bool saving = false;

        private List<string> files;

        private InkActionStack inkStack = new InkActionStack(100);

        private bool darkTheme;

        private Polyline lasso;
        private Line lassoConnectLine;
        private Rect boundingRect = Rect.Empty;
        private bool selecting = false;
        private bool movedSelection = false;
        private Point dragPoint;
        private StackPanel selectionPanel;
        private StackPanel rightTapPanel;

        public MainPage()
        {
            InitializeComponent();

            var coreTitleBar = CoreApplication.GetCurrentView().TitleBar;
            coreTitleBar.ExtendViewIntoTitleBar = true;

            Window.Current.SetTitleBar(appTitleBar);
            ApplicationView.GetForCurrentView().TitleBar.ButtonBackgroundColor = Colors.Transparent;

            ReadFiles();

            inkCanvas.InkPresenter.InputProcessingConfiguration.RightDragAction = 
                InkInputRightDragAction.LeaveUnprocessed;
            inkCanvas.InkPresenter.UnprocessedInput.PointerPressed +=
                UnprocessedInput_PointerPressed;
            inkCanvas.InkPresenter.UnprocessedInput.PointerMoved += (sender, args) =>
                UnprocessedInput_PointerMoved(sender, args, new Point(0,0));
            inkCanvas.InkPresenter.UnprocessedInput.PointerReleased +=
                UnprocessedInput_PointerReleased;

            // Listen for new ink or erase strokes to clean up selection UI.
            inkCanvas.InkPresenter.StrokeInput.StrokeStarted +=
                StrokeInput_StrokeStarted;
            inkCanvas.InkPresenter.StrokesErased +=
                InkPresenter_StrokesErased;

            inkCanvas.InkPresenter.StrokesCollected += (i, e) => { StrokesCollected(e); SaveInk(); };
            inkCanvas.InkPresenter.StrokesErased += (i, e) => { StrokesErased(e); SaveInk(); };
           
            inkCanvas.Loaded += InkLoaded;
            inkToolbar.Loaded += InkLoaded;

            inkToolbar.ActiveToolChanged += (i, e) => { UpdateInkAttributes(); };
            inkToolbar.InkDrawingAttributesChanged += (i, e) => { UpdateInkAttributes(); };
        }

        private void InkLoaded(object sender, RoutedEventArgs e)
        {
            if(!inkCanvas.IsLoaded || !inkToolbar.IsLoaded)
            {
                return;
            }
            /*//inkToolbar.InkDrawingAttributes.FitToCurve = false;
            InkDrawingAttributes ink = inkCanvas.InkPresenter.CopyDefaultDrawingAttributes();
            ink.ModelerAttributes.PredictionTime = TimeSpan.FromMilliseconds(0);
            ink.ModelerAttributes.UseVelocityBasedPressure = false;
            inkCanvas.InkPresenter.UpdateDefaultDrawingAttributes(ink);*/
            string theme = ApplicationData.Current.LocalSettings.Values["theme"] as string;
            if (!string.IsNullOrEmpty(theme))
            {
                darkTheme = bool.Parse(theme);
            }
            UpdateTheme();
        }

        private async void ReadFiles()
        {
            files = new List<string>();
            IReadOnlyList<StorageFile> localFiles = await ApplicationData.Current.LocalFolder.GetFilesAsync();
            
            foreach(StorageFile f in localFiles)
            {
                if(f.FileType == ".note")
                    AddFile(f.Path);
            }

            bool validLaunchFile = false;
            if (App.LaunchFile != null)
            {
                AddFile(App.LaunchFile.Path);
                validLaunchFile = true;
            }

            if (validLaunchFile)
            {
                StorageFile launchFile = await StorageFile.GetFileFromPathAsync(App.LaunchFile.Path);
                await LoadInk(launchFile, false);
            }
            else if(files.Count > 0)
            {
                await LoadLastInk();
            }
        }

        private void AddFile(string path)
        {
            if (files.Contains(path)) { return; }
            ListBoxItem item = new ListBoxItem();
            item.Content = System.IO.Path.GetFileNameWithoutExtension(path);
            notesList.Items.Add(item);
            files.Add(path);
            notesListContainer.Visibility = Visibility.Visible;
        }

        private void RemoveFile(string path)
        {
            if(!files.Contains(path)) { return; }
            int index = files.IndexOf(path);
            files.RemoveAt(index);
            notesList.Items.RemoveAt(index);
            notesList.SelectedIndex = 0;
            if(files.Count == 0)
            {
                notesListContainer.Visibility = Visibility.Collapsed;
            }
        }

        private void UpdateInkAttributes()
        {
            inkToolbar.InkDrawingAttributes.FitToCurve = true;
            InkDrawingAttributes ink = inkCanvas.InkPresenter.CopyDefaultDrawingAttributes();
            ink.ModelerAttributes.PredictionTime = TimeSpan.FromMilliseconds(0);
            inkCanvas.InkPresenter.UpdateDefaultDrawingAttributes(ink);
        }

        protected override void OnRightTapped(RightTappedRoutedEventArgs e)
        {
            base.OnRightTapped(e);
            if(boundingRect.IsEmpty)
                ShowRightTapPanel(e.GetPosition(canvasContainer));
        }

        protected override void OnTapped(TappedRoutedEventArgs e)
        {
            base.OnTapped(e);
            if (!boundingRect.IsEmpty)
            {
                ClearSelection();
            }
            if (rightTapPanel != null && rightTapPanel.Parent != null)
            {
                canvasContainer.Children.Remove(rightTapPanel);
            }
        }

        private void UnprocessedInput_PointerPressed(
          InkUnprocessedInput sender, PointerEventArgs args)
        {
            if (rightTapPanel != null && rightTapPanel.Parent != null)
            {
                canvasContainer.Children.Remove(rightTapPanel);
            }

            //inkCanvas.InkPresenter.IsInputEnabled = false;
            if (!boundingRect.IsEmpty && !boundingRect.Contains(args.CurrentPoint.RawPosition))
            {
                ClearSelection();
                movedSelection = false;
                return;
            }
            else if (!boundingRect.IsEmpty)
            {
                return;
            }

            // Initialize a selection lasso.
            lasso = new Polyline()
            {
                Stroke = new SolidColorBrush(darkTheme ? Colors.White : Colors.Gray),
                StrokeThickness = 1,
                StrokeDashArray = new DoubleCollection() { 5, 2 },
            };

            lasso.Points.Add(args.CurrentPoint.RawPosition);

            selectionCanvas.Children.Add(lasso);

            selecting = true;
        }

        private void UnprocessedInput_PointerMoved(
          InkUnprocessedInput sender, PointerEventArgs args, Point point)
        {
            if (args != null) point = args.CurrentPoint.RawPosition;
            if (!selecting && (boundingRect.Contains(point) || movedSelection))
            {
                if (!movedSelection)
                {
                    movedSelection = true;
                    dragPoint = point;
                    return;
                }

                Point pos = point;
                pos.X -= dragPoint.X;
                pos.Y -= dragPoint.Y;

                //ClearDrawnBoundingRect();
                if (boundingRect.X + pos.X + boundingRect.Width <= inkCanvas.Width && boundingRect.X + pos.X >= 0 && boundingRect.Y + pos.Y + boundingRect.Height <= inkCanvas.Height && boundingRect.Y + pos.Y >= 0)
                {
                    boundingRect = inkCanvas.InkPresenter.StrokeContainer.MoveSelected(pos);
                    dragPoint = point;
                    DrawBoundingRect(true);
                }
                
                movedSelection = true;
            }
            else if(selecting)
            {
                // Add a point to the lasso Polyline object.
                lasso.Points.Add(point);
                if(lassoConnectLine == null)
                {
                    lassoConnectLine = new Line();
                    lassoConnectLine.Stroke = new SolidColorBrush(darkTheme ? Colors.White : Colors.Gray);
                    selectionCanvas.Children.Add(lassoConnectLine);
                }
                lassoConnectLine.X1 = lasso.Points[0].X;
                lassoConnectLine.Y1 = lasso.Points[0].Y;
                lassoConnectLine.X2 = lasso.Points[lasso.Points.Count - 1].X;
                lassoConnectLine.Y2 = lasso.Points[lasso.Points.Count - 1].Y;
            }
            else
            {
                ClearSelection();
                selecting = false;
                movedSelection = false;
            }
        }

        private void UnprocessedInput_PointerReleased(
          InkUnprocessedInput sender, PointerEventArgs args)
        {
            // Add the final point to the Polyline object and
            // select strokes within the lasso area.
            // Draw a bounding box on the selection canvas
            // around the selected ink strokes.
            if (selecting)
            {
                lasso.Points.Add(args.CurrentPoint.RawPosition);

                boundingRect =
                  inkCanvas.InkPresenter.StrokeContainer.SelectWithPolyLine(
                    lasso.Points);

                DrawBoundingRect(false);
                //inkCanvas.InkPresenter.IsInputEnabled = false;
                inkCanvas.InkPresenter.InputProcessingConfiguration.Mode = InkInputProcessingMode.None;
            }
            else if (movedSelection)
            {
                SaveInk();
                DrawBoundingRect(false);
            }
            selecting = false;
            movedSelection = false;
        }

        private void DrawBoundingRect(bool moving)
        {
            // Clear all existing content from the selection canvas.
            selectionCanvas.Children.Clear();
            lassoConnectLine = null;

            // Draw a bounding rectangle only if there are ink strokes
            // within the lasso area.
            if (!((boundingRect.Width == 0) ||
              (boundingRect.Height == 0) ||
              boundingRect.IsEmpty))
            {
                var rectangle = new Rectangle()
                {
                    Stroke = new SolidColorBrush(darkTheme ? Colors.White : Colors.Gray),
                    StrokeThickness = 1,
                    StrokeDashArray = new DoubleCollection() { 5, 2 },
                    Width = boundingRect.Width,
                    Height = boundingRect.Height
                };

                Canvas.SetLeft(rectangle, boundingRect.X);
                Canvas.SetTop(rectangle, boundingRect.Y);

                if (!moving)
                {
                    ShowSelectionPanel();
                }
                else
                {
                    canvasContainer.Children.Remove(selectionPanel);
                }

                selectionCanvas.Children.Add(rectangle);
            }
        }

        private void ShowSelectionPanel()
        {
            selectionPanel = MakePanel(75, 40);

            Button deleteButton = MakePanelButton(new SolidColorBrush(Colors.Red), new Thickness(0, 0, 5, 0));
            deleteButton.Click += (x, y) => DeleteSelection();
            selectionPanel.Children.Add(deleteButton);

            Button copyButton = MakePanelButton(new SolidColorBrush(Colors.Blue), new Thickness(0, 0, 0, 0));
            copyButton.Click += (x, y) => inkCanvas.InkPresenter.StrokeContainer.CopySelectedToClipboard();
            selectionPanel.Children.Add(copyButton);

            selectionPanel.Margin = new Thickness(boundingRect.X + (boundingRect.Width / 2) - (selectionPanel.Width / 2), boundingRect.Y - selectionPanel.Height, 0, 0);

            if (!canvasContainer.Children.Contains(selectionPanel))
                canvasContainer.Children.Add(selectionPanel);
        }

        private void ShowRightTapPanel(Point point)
        {
            if(rightTapPanel != null)
            {
                canvasContainer.Children.Remove(rightTapPanel);
            }

            rightTapPanel = MakePanel(40, 40);

            Button pasteButton = MakePanelButton(new SolidColorBrush(Colors.Purple), new Thickness(0,0,0,0));
            pasteButton.Click += delegate { 
                if(inkCanvas.InkPresenter.StrokeContainer.CanPasteFromClipboard()) 
                    inkCanvas.InkPresenter.StrokeContainer.PasteFromClipboard(point); 
            };
            rightTapPanel.Children.Add(pasteButton);

            rightTapPanel.Margin = new Thickness(point.X, point.Y - rightTapPanel.Height, 0, 0);

            canvasContainer.Children.Add(rightTapPanel);
        }

        private StackPanel MakePanel(int width, int height)
        {
            StackPanel panel = new StackPanel();
            panel.Orientation = Orientation.Horizontal;
            panel.Background = (SolidColorBrush)Application.Current.Resources["ContentMaterialLow"];
            panel.CornerRadius = new CornerRadius(5);
            panel.VerticalAlignment = VerticalAlignment.Top;
            panel.HorizontalAlignment = HorizontalAlignment.Left;
            panel.Width = width;
            panel.Height = height;
            panel.Padding = new Thickness(5, 5, 5, 5);
            return panel;
        }

        private Button MakePanelButton(SolidColorBrush background, Thickness margin)
        {
            Button button = new Button();
            button.Width = 30;
            button.Height = 30;
            button.Margin = margin;
            button.Background = background;
            button.CornerRadius = new CornerRadius(5, 5, 5, 5);
            button.VerticalAlignment = VerticalAlignment.Center;
            button.HorizontalAlignment = HorizontalAlignment.Center;
            return button;
        }

        private void StrokeInput_StrokeStarted(
          InkStrokeInput sender, PointerEventArgs args)
        {
            if (boundingRect != Rect.Empty)
            {
                ClearSelection();
            }
            if(rightTapPanel != null && rightTapPanel.Parent != null)
            {
                canvasContainer.Children.Remove(rightTapPanel);
            }
        }

        private void InkPresenter_StrokesErased(
          InkPresenter sender, InkStrokesErasedEventArgs args)
        {
            if (boundingRect != Rect.Empty)
            {
                ClearSelection();
            }
            if (rightTapPanel != null && rightTapPanel.Parent != null)
            {
                canvasContainer.Children.Remove(rightTapPanel);
            }
        }

        private void ClearSelection()
        {
            var strokes = inkCanvas.InkPresenter.StrokeContainer.GetStrokes();
            foreach (var stroke in strokes)
            {
                stroke.Selected = false;
            }
            ClearDrawnBoundingRect();
            inkCanvas.InkPresenter.InputProcessingConfiguration.Mode = InkInputProcessingMode.Inking;
        }

        private void ClearDrawnBoundingRect()
        {
            boundingRect = Rect.Empty;
            if (selectionCanvas.Children.Any())
            {
                selectionCanvas.Children.Clear();
            }
            if(selectionPanel != null && selectionPanel.Parent != null)
            {
                canvasContainer.Children.Remove(selectionPanel);
            }
        }

        private void DeleteSelection()
        {
            List<InkStroke> strokes = new List<InkStroke>();
            foreach(InkStroke stroke in inkCanvas.InkPresenter.StrokeContainer.GetStrokes())
            {
                if (stroke.Selected)
                {
                    strokes.Add(stroke);
                }
            }
            inkCanvas.InkPresenter.StrokeContainer.DeleteSelected();
            inkStack.Push(strokes, true);
            ClearDrawnBoundingRect();
            inkCanvas.InkPresenter.InputProcessingConfiguration.Mode = InkInputProcessingMode.Inking;
            SaveInk();
        }

        private void ButtonToggleTheme(object sender, RoutedEventArgs e)
        {
            darkTheme = !darkTheme;
            ApplicationData.Current.LocalSettings.Values["theme"] = darkTheme.ToString();
            UpdateTheme();
        }

        private void UpdateTheme()
        {
            if (darkTheme)
            {
                foreach (InkStroke stroke in inkCanvas.InkPresenter.StrokeContainer.GetStrokes())
                {
                    if (stroke.DrawingAttributes.Color == Colors.Black)
                    {
                        InkDrawingAttributes da = stroke.DrawingAttributes;
                        da.Color = Colors.White;
                        stroke.DrawingAttributes = da;
                    }
                }
                
                Color lowColor = Tools.GetColorFromHexcode("#1b1b1b");
                Color highColor = Tools.GetColorFromHexcode("#1f1f1f");

                RequestedTheme = ElementTheme.Dark;

                ((SolidColorBrush)Application.Current.Resources["ContentMaterialLow"]).Color = lowColor;
                ((SolidColorBrush)Application.Current.Resources["ContentMaterialHigh"]).Color = highColor;
                ApplicationView.GetForCurrentView().TitleBar.ButtonForegroundColor = Colors.White;

                ((InkToolbarPenButton)inkToolbar.GetToolButton(InkToolbarTool.BallpointPen)).SelectedBrushIndex = 1;
                ((InkToolbarPencilButton)inkToolbar.GetToolButton(InkToolbarTool.Pencil)).SelectedBrushIndex = 1;
            }
            else
            {
                foreach (InkStroke stroke in inkCanvas.InkPresenter.StrokeContainer.GetStrokes())
                {
                    if (stroke.DrawingAttributes.Color == Colors.White)
                    {
                        InkDrawingAttributes da = stroke.DrawingAttributes;
                        da.Color = Colors.Black;
                        stroke.DrawingAttributes = da;
                    }
                }

                Color lowColor = Tools.GetColorFromHexcode("#efefef");
                Color highColor = Colors.White;

                RequestedTheme = ElementTheme.Light;

                ((SolidColorBrush)Application.Current.Resources["ContentMaterialLow"]).Color = lowColor;
                ((SolidColorBrush)Application.Current.Resources["ContentMaterialHigh"]).Color = highColor;
                ApplicationView.GetForCurrentView().TitleBar.ButtonForegroundColor = Colors.Black;

                ((InkToolbarPenButton)inkToolbar.GetToolButton(InkToolbarTool.BallpointPen)).SelectedBrushIndex = 0;
                ((InkToolbarPencilButton)inkToolbar.GetToolButton(InkToolbarTool.Pencil)).SelectedBrushIndex = 0;
            }
        }

        private void ToggleNotesPanel(object sender, RoutedEventArgs e)
        {
            if (notesPanel.Visibility == Visibility.Visible)
            {
                notesPanel.Visibility = Visibility.Collapsed;
                buttonNotesShow.Visibility = Visibility.Visible;
                buttonNotesHide.Visibility = Visibility.Collapsed;
                drawingCanvas.CornerRadius = new CornerRadius(0, 0, 0, 0);
                drawingCanvas.BorderThickness = new Thickness(0, 0.5, 0, 0);
            }
            else
            {
                notesPanel.Visibility = Visibility.Visible;   
                buttonNotesShow.Visibility = Visibility.Collapsed;
                buttonNotesHide.Visibility = Visibility.Visible;
                drawingCanvas.CornerRadius = new CornerRadius(5, 0, 0, 0);
                drawingCanvas.BorderThickness = new Thickness(0.5, 0.5, 0, 0);
            }
        }

        private async void NoteSelected(object sender, RoutedEventArgs e)
        {
            if(notesList.SelectedIndex < 0 || notesList.SelectedIndex > notesList.Items.Count)
            {
                return;
            }
            string path = files[notesList.SelectedIndex];
            try
            {
                StorageFile file = await ApplicationData.Current.LocalFolder.GetFileAsync(System.IO.Path.GetFileName(path));
                canvasScroll.ChangeView(0, 0, 1.0f);
                await LoadInk(file, false);
            }
            catch
            {
                RemoveFile(path);
                Debug.WriteLine("Error selecting note at " + path);
            }
        }

        private void SetNoteSelected(string fileName)
        {
            notesList.SelectionChanged -= NoteSelected;
            for (int i = 0; i < files.Count; i++)
            {
                if(System.IO.Path.GetFileName(files[i]) == fileName)
                {
                    notesList.SelectedIndex = i;
                    notesList.SelectionChanged += NoteSelected;
                    return;
                }
            }
            notesList.SelectionChanged += NoteSelected;
        }
        
        private void StrokesCollected(InkStrokesCollectedEventArgs e)
        {
            /*if (strokeAfterSelection && e.Strokes[0].StrokeDuration < TimeSpan.FromMilliseconds(100))
            {
                foreach(InkStroke s in e.Strokes)
                {
                    s.Selected = true;
                }
                inkCanvas.InkPresenter.StrokeContainer.DeleteSelected();
                strokeAfterSelection = false;
                return;
            }*/
            inkStack.Push(e.Strokes, false);
            //ResizeCanvas(canvasContainer.MaxWidth, 1000 + inkCanvas.InkPresenter.StrokeContainer.BoundingRect.Height);
        }

        private void StrokesErased(InkStrokesErasedEventArgs e)
        {
            inkStack.Push(e.Strokes, true);
            //ResizeCanvas(canvasContainer.MaxWidth, 1000 + inkCanvas.InkPresenter.StrokeContainer.BoundingRect.Height);
        }

        private void ButtonUndo_Click(object sender, RoutedEventArgs e)
        {
            if(inkStack.UndoCount > 0)
            {
                inkStack.Undo(inkCanvas.InkPresenter);
                SaveInk();
            }
        }

        private void ButtonRedo_Click(object sender, RoutedEventArgs e)
        {
            if(inkStack.RedoCount > 0)
            {
                inkStack.Redo(inkCanvas.InkPresenter);
                SaveInk();
            }
        }

        private void DrawRuledLines()
        {
            backgroundCanvas.Children.Clear();
            for(int i = 35; i < inkCanvas.Height; i += 35)
            {
                Line line = new Line();
                line.X1 = 10;
                line.Y1 = i;
                line.X2 = inkCanvas.Width - 10;
                line.Y2 = i;
                line.Stroke = new SolidColorBrush(Color.FromArgb(255, 200, 200, 200));
                line.StrokeThickness = 0.5;
                backgroundCanvas.Children.Add(line);
            }
        }

        private void DrawGridLines()
        {
            backgroundCanvas.Children.Clear();
            for(int x = 0; x < inkCanvas.Width; x += 35)
            {
                Line line = new Line();
                line.X1 = x;
                line.Y1 = 0;
                line.X2 = x;
                line.Y2 = inkCanvas.Height;
                line.Stroke = new SolidColorBrush(Color.FromArgb(255, 200, 200, 200));
                line.StrokeThickness = 0.5;
                backgroundCanvas.Children.Add(line);
            }
            for (int y = 0; y < inkCanvas.Height; y += 35)
            {
                Line line = new Line();
                line.X1 = 0;
                line.Y1 = y;
                line.X2 = inkCanvas.Width;
                line.Y2 = y;
                line.Stroke = new SolidColorBrush(Color.FromArgb(255, 200, 200, 200));
                line.StrokeThickness = 0.5;
                backgroundCanvas.Children.Add(line);
            }
        }

        private void DrawDots()
        {
            backgroundCanvas.Children.Clear();

            for (int x = 35; x < inkCanvas.Width; x += 35)
            {
                Line line = new Line();
                line.X1 = x;
                line.Y1 = 35;
                line.X2 = x;
                line.Y2 = inkCanvas.Height - 35;
                line.Stroke = new SolidColorBrush(Color.FromArgb(255, 200, 200, 200));
                line.StrokeThickness = 2;
                line.StrokeDashArray = new DoubleCollection() { 1, 17.5 };
                backgroundCanvas.Children.Add(line);
            }
        }

        private async void CanvasTypeComboChanged(object sender, RoutedEventArgs e)
        {
            if (canvasContainer == null) { return; }

            if(canvasTypeCombo.SelectedItem == canvasType_Grid)
            {
                DrawGridLines();
            }
            else if(canvasTypeCombo.SelectedItem == canvasType_Ruled)
            {
                DrawRuledLines();
            }
            else if(canvasTypeCombo.SelectedItem == canvasType_Dotted)
            {
                DrawDots();
            }
            else
            {
                backgroundCanvas.Children.Clear();
            }

            if(sender != null) await SaveCanvasType();
        }
        
        private void TriggerResizeCanvas(object sender, RoutedEventArgs e)
        {
            double width = inkCanvas.Width;
            double height = inkCanvas.Height;
            if (!string.IsNullOrEmpty(canvasXInput.Text))
            {
                double.TryParse(canvasXInput.Text, out width);
            }
            if (!string.IsNullOrEmpty(canvasYInput.Text))
            {
                double.TryParse(canvasYInput.Text, out height);
            }

            ResizeCanvas(width, height);
            //if (sender != null) saveCanvasSize();
        }

        private void ResizeCanvas(double width, double height)
        {
            width = Math.Clamp(width, 500, 10000);
            width = Math.Floor(width / 35.0) * 35.0;
            height = Math.Clamp(height, 500, 50000);
            height = Math.Floor(height / 35.0) * 35.0;
            inkCanvas.Width = width;
            inkCanvas.Height = height;
            canvasContainer.MaxWidth = width;
            canvasContainer.MaxHeight = height;
            CanvasTypeComboChanged(null, null);
        }

        private void ButtonCenterAlign_Click(object sender, RoutedEventArgs e)
        {
            canvasContainer.HorizontalAlignment = HorizontalAlignment.Center;
        }

        private void ButtonRightAlign_Click(object sender, RoutedEventArgs e)
        {
            canvasContainer.HorizontalAlignment = HorizontalAlignment.Right;
        }

        private void ButtonLeftAlign_Click(object sender, RoutedEventArgs e)
        {
            canvasContainer.HorizontalAlignment = HorizontalAlignment.Left;
        }

        private void ButtonZoomReset_Click(object sender, RoutedEventArgs e)
        {
            canvasScroll.ChangeView(canvasScroll.HorizontalOffset, canvasScroll.VerticalOffset, 1.0f);
        }

        private async void ButtonNew_Click(object sender, RoutedEventArgs e)
        {
            string fileName = newNoteNameBox.Text;
            if (!string.IsNullOrEmpty(fileName))
            {
                foreach(char c in System.IO.Path.GetInvalidFileNameChars())
                {
                    if (fileName.Contains(c))
                    {
                        newNoteNameBox.Text = "Invalid name...";
                        return;
                    }
                }
            }
            else
            {
                newNoteNameBox.PlaceholderText = "Enter note name...";
                return;
            }

            try
            {
                StorageFile file = await ApplicationData.Current.LocalFolder.CreateFileAsync(fileName + ".note");
                ApplicationData.Current.LocalSettings.Values["lastFileToken"] = StorageApplicationPermissions.FutureAccessList.Add(file);
                await LoadInk(file, true);
                inkCanvas.InkPresenter.StrokeContainer.Clear();
                canvasTypeCombo.SelectedIndex = 0;
                //ResizeCanvas(1000, 1000);
                CanvasTypeComboChanged(null, null);
                await SaveCanvasType();
                SaveInk();
            }
            catch
            {
                Debug.WriteLine("Error making new file " + fileName);
            }
        }

        private async void ButtonSave_Click(object sender, RoutedEventArgs e)
        {
            // Get all strokes on the InkCanvas.
            IReadOnlyList<InkStroke> currentStrokes = inkCanvas.InkPresenter.StrokeContainer.GetStrokes();

            if (currentStrokes.Count > 0)
            {
                // Use a file picker to identify ink file.
                Windows.Storage.Pickers.FileSavePicker savePicker =
                    new Windows.Storage.Pickers.FileSavePicker();
                savePicker.SuggestedStartLocation =
                    Windows.Storage.Pickers.PickerLocationId.DocumentsLibrary;
                savePicker.FileTypeChoices.Add(
                    "Notes File",
                    new List<string>() { ".note" });
                savePicker.DefaultFileExtension = ".note";
                savePicker.SuggestedFileName = DateTime.Now.Month + "-" + DateTime.Now.Day + "-" + DateTime.Now.Year + " Notes";

                // Show the file picker.
                StorageFile file =
                    await savePicker.PickSaveFileAsync();
                // When selected, picker returns a reference to the file.
                if (file != null)
                {
                    ApplicationDataContainer localSettings = ApplicationData.Current.LocalSettings;
                    string faToken = localSettings.Values["fileToken"] as string;
                    if (!string.IsNullOrEmpty(faToken))
                    {
                        StorageApplicationPermissions.FutureAccessList.AddOrReplace(faToken, file);
                    }
                    else
                    {
                        localSettings.Values["fileToken"] = StorageApplicationPermissions.FutureAccessList.Add(file);
                    }

                    // Prevent updates to the file until updates are 
                    // finalized with call to CompleteUpdatesAsync.
                    CachedFileManager.DeferUpdates(file);
                    // Open a file stream for writing.
                    IRandomAccessStream stream = await file.OpenAsync(FileAccessMode.ReadWrite);
                    // Write the ink strokes to the output stream.
                    using(IOutputStream outputStream = stream.GetOutputStreamAt(0))
                    {
                        StreamWriter sw = new StreamWriter(outputStream.AsStreamForWrite());
                        int canvasType = canvasTypeCombo.SelectedIndex;
                        await sw.WriteAsync("" + canvasType);
                        await sw.FlushAsync();
                        sw.Close();
                    }
                    using (IOutputStream outputStream = stream.GetOutputStreamAt(2))
                    {
                        await inkCanvas.InkPresenter.StrokeContainer.SaveAsync(outputStream);
                        await outputStream.FlushAsync();
                    }
                    
                    stream.Dispose();

                    // Finalize write so other apps can update file.
                    Windows.Storage.Provider.FileUpdateStatus status =
                        await CachedFileManager.CompleteUpdatesAsync(file);

                    if (status == Windows.Storage.Provider.FileUpdateStatus.Complete)
                    {
                        // File saved.
                    }
                    else
                    {
                        // File couldn't be saved.
                    }
                }
                // User selects Cancel and picker returns null.
                else
                {
                    // Operation cancelled.
                }
            }
        }

        private async void ButtonLoad_Click(object sender, RoutedEventArgs e)
        {
            // Use a file picker to identify ink file.
            Windows.Storage.Pickers.FileOpenPicker openPicker =
                new Windows.Storage.Pickers.FileOpenPicker();
            openPicker.SuggestedStartLocation =
                Windows.Storage.Pickers.PickerLocationId.DocumentsLibrary;
            openPicker.FileTypeFilter.Add(".note");
            // Show the file picker.
            StorageFile file = await openPicker.PickSingleFileAsync();
            await LoadInk(file, false);
        }

        private async Task LoadLastInk()
        {
            ApplicationDataContainer localSettings = ApplicationData.Current.LocalSettings;
            if (!string.IsNullOrEmpty(localSettings.Values["lastFileToken"] as string))
            {
                try
                {
                    StorageFile file = await StorageApplicationPermissions.FutureAccessList.GetFileAsync(localSettings.Values["lastFileToken"] as string);
                    await LoadInk(file, false);
                }
                catch
                {
                    Debug.WriteLine("Failed to load last file");
                    localSettings.Values["lastFileToken"] = "";
                }
            }
        }

        private async Task LoadInk(StorageFile file, bool newFile)
        {
            if (file != null && !newFile)
            {
                try
                {
                    canvasContainer.Visibility = Visibility.Visible;

                    // Open a file stream for reading.
                    IRandomAccessStream stream = await file.OpenAsync(FileAccessMode.Read);
                    // Read from file.
                    using (var inputStream = stream.GetInputStreamAt(0))
                    {
                        StreamReader sr = new StreamReader(inputStream.AsStreamForRead());
                        char[] chars = new char[1];
                        sr.Read(chars, 0, chars.Length);
                        int canvasType = int.Parse(new string(chars));
                        canvasTypeCombo.SelectionChanged -= CanvasTypeComboChanged;
                        canvasTypeCombo.SelectedIndex = canvasType;
                        CanvasTypeComboChanged(null, null);
                        canvasTypeCombo.SelectionChanged += CanvasTypeComboChanged;
                        sr.Close();
                    }
                    using (var inputStream = stream.GetInputStreamAt(2))
                    {
                        await inkCanvas.InkPresenter.StrokeContainer.LoadAsync(inputStream);
                    }
                    stream.Dispose();
                } 
                catch(Exception ex)
                {
                    Debug.WriteLine("Failed to load file");
                    Debug.WriteLine(ex.Message);
                    ApplicationData.Current.LocalSettings.Values["lastFileToken"] = "";
                    if (files.Contains(file.Path))
                    {
                        RemoveFile(file.Path);
                    }
                    canvasContainer.Visibility = Visibility.Collapsed;
                    return;
                }

                ResizeCanvas(canvasContainer.MaxWidth, 1000 + inkCanvas.InkPresenter.StrokeContainer.BoundingRect.Height);

                ApplicationDataContainer localSettings = ApplicationData.Current.LocalSettings;
                string token = StorageApplicationPermissions.FutureAccessList.Add(file);
                localSettings.Values["lastFileToken"] = token;

                if (!files.Contains(file.Path))
                {
                    AddFile(file.Path);
                }
                UpdateTheme();
                SetNoteSelected(file.Name);
            }
            else
            {
                ApplicationData.Current.LocalSettings.Values["lastFileToken"] = StorageApplicationPermissions.FutureAccessList.Add(file);
                AddFile(file.Path);
                SetNoteSelected(file.Name);
                canvasContainer.Visibility = Visibility.Visible;
            }
        }

        private void InkCleared(InkToolbar toolbar, object sender) { SaveInk(); }

        private async Task SaveCanvasType()
        {
            StorageFile file = null;
            ApplicationDataContainer localSettings = ApplicationData.Current.LocalSettings;
            if (!string.IsNullOrEmpty(localSettings.Values["lastFileToken"] as string))
            {
                try
                {
                    file = await StorageApplicationPermissions.FutureAccessList.GetFileAsync(localSettings.Values["lastFileToken"] as string);
                }
                catch (FileNotFoundException e)
                {
                    Debug.WriteLine(e.Message);
                    localSettings.Values["lastFileToken"] = "";
                }
            }

            if (file == null) { return; }

            // Prevent updates to the file until updates are 
            // finalized with call to CompleteUpdatesAsync.
            CachedFileManager.DeferUpdates(file);
            // Open a file stream for writing.
            IRandomAccessStream stream = await file.OpenAsync(FileAccessMode.ReadWrite);
            // Write the ink strokes to the output stream.
            using (IOutputStream outputStream = stream.GetOutputStreamAt(0))
            {
                StreamWriter sw = new StreamWriter(outputStream.AsStreamForWrite());
                int canvasType = canvasTypeCombo.SelectedIndex;
                await sw.WriteAsync("" + canvasType);
                await sw.FlushAsync();
                sw.Close();
            }
            stream.Dispose();

            // Finalize write so other apps can update file.
            Windows.Storage.Provider.FileUpdateStatus status =
                await CachedFileManager.CompleteUpdatesAsync(file);

            if (status == Windows.Storage.Provider.FileUpdateStatus.Complete)
            {
                // File saved.
            }
            else
            {
                // File couldn't be saved.
            }
        }

        private async void SaveCanvasSize()
        {
            StorageFile file = null;
            ApplicationDataContainer localSettings = ApplicationData.Current.LocalSettings;
            if (!string.IsNullOrEmpty(localSettings.Values["lastFileToken"] as string))
            {
                try
                {
                    file = await StorageApplicationPermissions.FutureAccessList.GetFileAsync(localSettings.Values["lastFileToken"] as string);
                }
                catch (FileNotFoundException e)
                {
                    Debug.WriteLine(e.Message);
                    localSettings.Values["lastFileToken"] = "";
                }
            }

            if (file == null) { return; }

            // Prevent updates to the file until updates are 
            // finalized with call to CompleteUpdatesAsync.
            CachedFileManager.DeferUpdates(file);
            // Open a file stream for writing.
            IRandomAccessStream stream = await file.OpenAsync(FileAccessMode.ReadWrite);
            // Write the ink strokes to the output stream.
            using (IOutputStream outputStream = stream.GetOutputStreamAt(2))
            {
                StreamWriter sw = new StreamWriter(outputStream.AsStreamForWrite());
                await sw.WriteAsync("" + (int)inkCanvas.Width);
                await sw.WriteAsync("" + (int)inkCanvas.Height);
                await sw.FlushAsync();
                sw.Close();
            }
            stream.Dispose();

            // Finalize write so other apps can update file.
            Windows.Storage.Provider.FileUpdateStatus status =
                await CachedFileManager.CompleteUpdatesAsync(file);

            if (status == Windows.Storage.Provider.FileUpdateStatus.Complete)
            {
                // File saved.
            }
            else
            {
                // File couldn't be saved.
            }
        }

        private async void SaveInk()
        {
            if (saving) { return; }
            else saving = true;
            
            StorageFile file = null;
            ApplicationDataContainer localSettings = ApplicationData.Current.LocalSettings;
            if (!string.IsNullOrEmpty(localSettings.Values["lastFileToken"] as string))
            {
                try
                {
                    file = await StorageApplicationPermissions.FutureAccessList.GetFileAsync(localSettings.Values["lastFileToken"] as string);
                }
                catch(FileNotFoundException e)
                {
                    Debug.WriteLine("Failed to save file");
                    Debug.WriteLine(e.Message);
                    localSettings.Values["lastFileToken"] = "";
                }
            }

            if(file == null) { saving = false; return; }

            //Debug.WriteLine("Saving");
            
            saveIcon.Visibility = Visibility.Visible;

            await Task.Delay(500);

            // Prevent updates to the file until updates are 
            // finalized with call to CompleteUpdatesAsync.
            CachedFileManager.DeferUpdates(file);
            // Open a file stream for writing.
            IRandomAccessStream stream = await file.OpenAsync(FileAccessMode.ReadWrite);
            // Write the ink strokes to the output stream.
            using (IOutputStream outputStream = stream.GetOutputStreamAt(2))
            {
                await inkCanvas.InkPresenter.StrokeContainer.SaveAsync(outputStream);
                await outputStream.FlushAsync();
            }
            stream.Dispose();

            // Finalize write so other apps can update file.
            Windows.Storage.Provider.FileUpdateStatus status =
                await CachedFileManager.CompleteUpdatesAsync(file);
            
            if (status == Windows.Storage.Provider.FileUpdateStatus.Complete)
            {
                // File saved.
            }
            else
            {
                // File couldn't be saved.
            }

            ResizeCanvas(canvasContainer.MaxWidth, 1000 + inkCanvas.InkPresenter.StrokeContainer.BoundingRect.Height);
            saveIcon.Visibility = Visibility.Collapsed;
            saving = false;
        }
    }
}