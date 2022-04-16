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
        private Rect boundingRect = Rect.Empty;
        private bool selecting = false;
        private bool movedSelection = false;
        private bool pressedAfterSelection = false;
        private Point dragPoint;
        private Button selectionDeleteButton;

        private bool strokeAfterSelection = false;

        public MainPage()
        {
            InitializeComponent();

            var coreTitleBar = CoreApplication.GetCurrentView().TitleBar;
            coreTitleBar.ExtendViewIntoTitleBar = true;

            Window.Current.SetTitleBar(appTitleBar);
            ApplicationView.GetForCurrentView().TitleBar.ButtonBackgroundColor = Colors.Transparent;

            //UPDATES THEME AFTER LOADING INK
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
            StorageFile file = null;
            try
            {
                file = await ApplicationData.Current.LocalFolder.GetFileAsync("filePaths.txt");
            }
            catch
            {
                Debug.Write("Error opening filePaths.txt");
            }

            List<string> tempFiles = new List<string>();

            if (file != null)
            {
                var stream = await file.OpenAsync(FileAccessMode.Read);
                ulong size = stream.Size;

                using (var inputStream = stream.GetInputStreamAt(0))
                {
                    using (var dataReader = new DataReader(inputStream))
                    {
                        uint numBytesLoaded = await dataReader.LoadAsync((uint)size);
                        string text = dataReader.ReadString(numBytesLoaded);
                        string[] arr = text.Split('\n');
                        for (int i = 0; i < arr.Length; i++)
                        {
                            arr[i] = arr[i].Replace("\n", String.Empty);
                            arr[i] = arr[i].Replace("\r", String.Empty);
                        }
                        tempFiles.AddRange(arr);
                        dataReader.Dispose();
                    }
                    inputStream.Dispose();
                }
                stream.Dispose();
                
                foreach (string s in tempFiles)
                {
                    if (string.IsNullOrEmpty(s) || files.Contains(s)) { continue; }

                    try
                    {
                        //StorageFile fileTemp = await ApplicationData.Current.LocalFolder.GetFileAsync(System.IO.Path.GetFileName(s));
                        StorageFile fileTemp = await StorageApplicationPermissions.FutureAccessList.GetFileAsync((string)ApplicationData.Current.LocalSettings.Values[s]);
                    }
                    catch (Exception e)
                    {
                        Debug.WriteLine(e.Message);
                        if (files.Contains(s))
                        {
                            files.Remove(s);
                        }
                        if (ApplicationData.Current.LocalSettings.Values.ContainsKey(s))
                        {
                            ApplicationData.Current.LocalSettings.Values.Remove(s);
                        }
                        continue;
                    }

                    await AddFile(s, false);
                    files.Add(s);
                }

                bool validLaunchFile = false;
                if (App.LaunchFile != null)
                {
                    await AddFile(App.LaunchFile.Path, true);
                    validLaunchFile = true;
                }

                await WriteFiles();
                if (validLaunchFile)
                {
                    string faToken = StorageApplicationPermissions.FutureAccessList.Add(App.LaunchFile);
                    ApplicationData.Current.LocalSettings.Values[App.LaunchFile.Path] = faToken;
                    StorageFile launchFile = await StorageApplicationPermissions.FutureAccessList.GetFileAsync(faToken);
                    await LoadInk(launchFile);
                }
                else
                {
                    await LoadLastInk();
                }

                UpdateTheme();
            }
        }

        private async Task WriteFiles()
        {
            StorageFile file = await ApplicationData.Current.LocalFolder.CreateFileAsync("filePaths.txt",
                CreationCollisionOption.ReplaceExisting);

            CachedFileManager.DeferUpdates(file);
            // Open a file stream for writing.
            var stream = await file.OpenAsync(FileAccessMode.ReadWrite);
            // Write the ink strokes to the output stream.
            using (IOutputStream outputStream = stream.GetOutputStreamAt(0))
            {
                StreamWriter sw = new StreamWriter(outputStream.AsStreamForWrite());
                foreach(string s in files)
                {
                    await sw.WriteLineAsync(s);
                }
                await sw.FlushAsync();
                sw.Close();
            }
            stream.Dispose();

            // Finalize write so other apps can update file.
            Windows.Storage.Provider.FileUpdateStatus status =
                await CachedFileManager.CompleteUpdatesAsync(file);

            //await FileIO.WriteLinesAsync(file, files);
        }

        private async Task AddFile(string path, bool addToList)
        {
            if (addToList && files.Contains(path)) { return; }
            ListBoxItem item = new ListBoxItem();
            item.Content = System.IO.Path.GetFileNameWithoutExtension(path);
            notesList.Items.Add(item);
            if (addToList)
            {
                files.Add(path);
                await WriteFiles();
            }
        }

        private void UpdateInkAttributes()
        {
            inkToolbar.InkDrawingAttributes.FitToCurve = true;
            InkDrawingAttributes ink = inkCanvas.InkPresenter.CopyDefaultDrawingAttributes();
            ink.ModelerAttributes.PredictionTime = TimeSpan.FromMilliseconds(0);
            inkCanvas.InkPresenter.UpdateDefaultDrawingAttributes(ink);
        }

        private void UnprocessedInput_PointerPressed(
          InkUnprocessedInput sender, PointerEventArgs args)
        {
            //inkCanvas.InkPresenter.IsInputEnabled = false;
            if(!boundingRect.IsEmpty && !boundingRect.Contains(args.CurrentPoint.RawPosition))
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

                boundingRect = inkCanvas.InkPresenter.StrokeContainer.MoveSelected(pos);
                dragPoint = point;

                DrawBoundingRect(true);
                
                movedSelection = true;
            }
            else if(selecting)
            {
                // Add a point to the lasso Polyline object.
                lasso.Points.Add(point);
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
                //lasso.Points.Add(args.CurrentPoint.RawPosition);

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

        private void CanvasPointerPressed(PointerRoutedEventArgs args)
        {
            if (args.Pointer.PointerDeviceType == Windows.Devices.Input.PointerDeviceType.Pen)
            {
                Debug.WriteLine("pressed");
                pressedAfterSelection = true;
            }
        }

        private void CanvasPointerMoved(PointerRoutedEventArgs args)
        {
            if (!pressedAfterSelection) return;

            if (args.Pointer.PointerDeviceType == Windows.Devices.Input.PointerDeviceType.Pen)
            {
                Debug.WriteLine("moved");
                UnprocessedInput_PointerMoved(null, null, args.GetCurrentPoint(inkCanvas).RawPosition);
                /*if (!selecting && (boundingRect.Contains(args.GetCurrentPoint(inkCanvas).RawPosition) || movedSelection))
                {
                    if (!movedSelection)
                    {
                        movedSelection = true;
                        dragPoint = args.GetCurrentPoint(inkCanvas).RawPosition;
                        return;
                    }

                    Point pos = args.GetCurrentPoint(inkCanvas).RawPosition;
                    pos.X -= dragPoint.X;
                    pos.Y -= dragPoint.Y;

                    //ClearDrawnBoundingRect();

                    boundingRect = inkCanvas.InkPresenter.StrokeContainer.MoveSelected(pos);
                    dragPoint = args.GetCurrentPoint(inkCanvas).RawPosition;

                    DrawBoundingRect(true);

                    movedSelection = true;
                }
                else
                {
                    ClearSelection();
                    selecting = false;
                    movedSelection = false;
                }*/
            }
        }

        private void CanvasPointerReleased(PointerRoutedEventArgs args)
        {
            if (!pressedAfterSelection) return;

            if (args.Pointer.PointerDeviceType == Windows.Devices.Input.PointerDeviceType.Pen)
            {
                Debug.WriteLine("released");
                UnprocessedInput_PointerReleased(null, null);
                /*if (movedSelection)
                {
                    SaveInk();
                    DrawBoundingRect(false);
                }
                selecting = false;
                movedSelection = false;
                pressedAfterSelection = false;*/
                pressedAfterSelection = false;
            }
        }

        private void DrawBoundingRect(bool moving)
        {
            // Clear all existing content from the selection canvas.
            selectionCanvas.Children.Clear();

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
                    if (selectionDeleteButton == null)
                    {
                        selectionDeleteButton = new Button();
                        selectionDeleteButton.Width = 30;
                        selectionDeleteButton.Height = 30;
                        selectionDeleteButton.Background = new SolidColorBrush(Colors.Red);
                        selectionDeleteButton.VerticalAlignment = VerticalAlignment.Top;
                        selectionDeleteButton.CornerRadius = new CornerRadius(0, 5, 5, 0);
                        selectionDeleteButton.Click += (x, y) => DeleteSelection();
                    }
                    selectionDeleteButton.Margin = new Thickness(boundingRect.X + boundingRect.Width, boundingRect.Y, 0, 0);
                    if(!canvasContainer.Children.Contains(selectionDeleteButton))
                        canvasContainer.Children.Add(selectionDeleteButton);
                }
                else
                {
                    canvasContainer.Children.Remove(selectionDeleteButton);
                }

                selectionCanvas.Children.Add(rectangle);
            }
        }

        private void StrokeInput_StrokeStarted(
          InkStrokeInput sender, PointerEventArgs args)
        {
            if (boundingRect != Rect.Empty)
            {
                strokeAfterSelection = true;
                ClearSelection();
            }
        }

        private void InkPresenter_StrokesErased(
          InkPresenter sender, InkStrokesErasedEventArgs args)
        {
            if(boundingRect != Rect.Empty)
                ClearSelection();
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
            if(selectionDeleteButton != null && selectionDeleteButton.Parent != null)
            {
                canvasContainer.Children.Remove(selectionDeleteButton);
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
            string path = files[notesList.SelectedIndex];
            StorageFile file = null;
            if (ApplicationData.Current.LocalSettings.Values.ContainsKey(path))
            {
                file = await StorageApplicationPermissions.FutureAccessList.GetFileAsync((string)ApplicationData.Current.LocalSettings.Values[path]);
            }
            else
            {
                return;
            }
            canvasScroll.ChangeView(0, 0, 1.0f);
            await LoadInk(file);
        }
        
        private void StrokesCollected(InkStrokesCollectedEventArgs e)
        {
            if (strokeAfterSelection && e.Strokes[0].StrokeDuration < TimeSpan.FromMilliseconds(100))
            {
                foreach(InkStroke s in e.Strokes)
                {
                    s.Selected = true;
                }
                inkCanvas.InkPresenter.StrokeContainer.DeleteSelected();
                strokeAfterSelection = false;
                return;
            }
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

        private void CanvasTypeComboChanged(object sender, RoutedEventArgs e)
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
            else
            {
                backgroundCanvas.Children.Clear();
            }

            if(sender != null) SaveCanvasType();
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
            height = Math.Clamp(height, 500, 50000);
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

                    /*BinaryFormatter bf = new BinaryFormatter();
                    Stream stream = await file.OpenStreamForWriteAsync();
                    bf.Serialize(stream, inkCanvas.InkPresenter.StrokeContainer.GetStrokes());
                    stream.Dispose();*/

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
            await LoadInk(file);
        }

        private async Task LoadLastInk()
        {
            ApplicationDataContainer localSettings = ApplicationData.Current.LocalSettings;
            if (!string.IsNullOrEmpty(localSettings.Values["lastFileToken"] as string))
            {
                try
                {
                    StorageFile file = await StorageApplicationPermissions.FutureAccessList.GetFileAsync(localSettings.Values["lastFileToken"] as string);
                    await LoadInk(file);
                }
                catch
                {
                    localSettings.Values["lastFileToken"] = "";
                }
            }
        }

        private async Task LoadInk(StorageFile file)
        {
            if (file != null)
            {
                try
                {
                    // Open a file stream for reading.
                    IRandomAccessStream stream = await file.OpenAsync(FileAccessMode.Read);
                    // Read from file.
                    using (var inputStream = stream.GetInputStreamAt(0))
                    {
                        StreamReader sr = new StreamReader(inputStream.AsStreamForRead());
                        char[] chars = new char[1];
                        sr.Read(chars, 0, chars.Length);
                        int canvasType = int.Parse(new string(chars));
                        canvasTypeCombo.SelectedIndex = canvasType;
                        CanvasTypeComboChanged(null, null);
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
                    Debug.WriteLine(ex.Message);
                    ApplicationData.Current.LocalSettings.Values["lastFileToken"] = "";
                    return;
                }

                ResizeCanvas(canvasContainer.MaxWidth, 1000 + inkCanvas.InkPresenter.StrokeContainer.BoundingRect.Height);

                ApplicationDataContainer localSettings = ApplicationData.Current.LocalSettings;
                localSettings.Values["lastFileToken"] = StorageApplicationPermissions.FutureAccessList.Add(file);

                localSettings.Values[file.Path] = (string)localSettings.Values["lastFileToken"];

                if (!files.Contains(file.Path))
                {
                    await AddFile(file.Path, true);
                }
            }
        }

        private void InkCleared(InkToolbar toolbar, object sender) { SaveInk(); }

        private async void SaveCanvasType()
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