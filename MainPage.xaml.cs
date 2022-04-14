using System;
using System.Collections.Generic;
using Windows.UI.Xaml;
using Windows.UI.Xaml.Controls;
using Windows.UI.Input.Inking;
using Windows.Storage.Streams;
using Windows.UI.Xaml.Input;
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
        private Point dragPoint;
        private Button selectionDeleteButton;

        private bool strokeAfterSelection = false;

        public MainPage()
        {
            InitializeComponent();

            canvasScroll.AddHandler(PointerPressedEvent,
                new PointerEventHandler(myScrollViewer_PointerPressed),
                true /*handledEventsToo*/);
            canvasScroll.AddHandler(PointerReleasedEvent,
                new PointerEventHandler(myScrollViewer_PointerReleased),
                true /*handledEventsToo*/);
            canvasScroll.AddHandler(PointerCanceledEvent,
                new PointerEventHandler(myScrollViewer_PointerCanceled),
                true /*handledEventsToo*/);

            var coreTitleBar = CoreApplication.GetCurrentView().TitleBar;
            coreTitleBar.ExtendViewIntoTitleBar = true;

            Window.Current.SetTitleBar(appTitleBar);
            ApplicationView.GetForCurrentView().TitleBar.ButtonBackgroundColor = Colors.Transparent;

            readFiles();

            inkCanvas.InkPresenter.InputProcessingConfiguration.RightDragAction = 
                InkInputRightDragAction.LeaveUnprocessed;
            // Listen for unprocessed pointer events from modified input.
            // The input is used to provide selection functionality.
            inkCanvas.InkPresenter.UnprocessedInput.PointerPressed +=
                UnprocessedInput_PointerPressed;
            inkCanvas.InkPresenter.UnprocessedInput.PointerMoved +=
                UnprocessedInput_PointerMoved;
            inkCanvas.InkPresenter.UnprocessedInput.PointerReleased +=
                UnprocessedInput_PointerReleased;

            // Listen for new ink or erase strokes to clean up selection UI.
            inkCanvas.InkPresenter.StrokeInput.StrokeStarted +=
                StrokeInput_StrokeStarted;
            inkCanvas.InkPresenter.StrokesErased +=
                InkPresenter_StrokesErased;

            inkCanvas.InkPresenter.StrokesCollected += (i, e) => { strokeCollected(e); saveInk(); };
            inkCanvas.InkPresenter.StrokesErased += (i, e) => { strokeErased(e); saveInk(); };
           
            inkCanvas.Loaded += inkLoaded;
            inkToolbar.Loaded += inkLoaded;

            inkToolbar.ActiveToolChanged += (i, e) => { UpdateInkAttributes(); };
            inkToolbar.InkDrawingAttributesChanged += (i, e) => { UpdateInkAttributes(); };

            //canvasScroll.PointerMoved += (o, a) => { CanvasPointerMoved(a); };
        }

        private void inkLoaded(object sender, RoutedEventArgs e)
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
            updateTheme();
        }

        private async void readFiles()
        {
            files = new List<string>();
            StorageFile file = null;
            try
            {
                file = await ApplicationData.Current.LocalFolder.GetFileAsync("filePaths.txt");
            }
            catch(FileNotFoundException e)
            {
                Debug.Write(e.Message);
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
                        /*int closestDirDivider = 0;
                        for(int i = 0; i < s.Length; i++)
                        {
                            if(s[i] == '\\')
                            {
                                closestDirDivider = i;
                            }
                        }
                        string fileName = s.Substring(closestDirDivider + 1);
                        Debug.WriteLine(fileName);*/
                        StorageFile fileTemp = await ApplicationData.Current.LocalFolder.GetFileAsync(System.IO.Path.GetFileName(s));
                        //StorageFile fileTemp = await StorageFile.GetFileFromPathAsync(s);
                    }
                    catch (Exception e)
                    {
                        Debug.WriteLine(e.Message);
                        if (files.Contains(s))
                        {
                            files.Remove(s);
                        }
                        continue;
                    }

                    Debug.WriteLine("adding file: " + s);
                    await addFile(s, false);
                    files.Add(s);
                }

                await writeFiles();
                loadLastInk();
            }
        }

        private async Task writeFiles()
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

        private async Task addFile(string path, bool addToList)
        {
            if (addToList && files.Contains(path)) { return; }
            ListBoxItem item = new ListBoxItem();
            item.Content = System.IO.Path.GetFileNameWithoutExtension(path);
            notesList.Items.Add(item);
            if (addToList)
            {
                files.Add(path);
                await writeFiles();
            }
        }

        private void UpdateInkAttributes()
        {
            inkToolbar.InkDrawingAttributes.FitToCurve = true;
            InkDrawingAttributes ink = inkCanvas.InkPresenter.CopyDefaultDrawingAttributes();
            ink.ModelerAttributes.PredictionTime = TimeSpan.FromMilliseconds(0);
            inkCanvas.InkPresenter.UpdateDefaultDrawingAttributes(ink);
        }

        /*private void CanvasPointerMoved(PointerRoutedEventArgs args)
        {
            if(args.Pointer.PointerDeviceType == Windows.Devices.Input.PointerDeviceType.Pen)
            {
                buttonInsert.Margin = new Thickness(-buttonInsert.Width, args.GetCurrentPoint(canvasContainer).RawPosition.Y - (buttonInsert.Height / 2), 0, 0);
            }
        }*/

        // Handle unprocessed pointer events from modified input.
        // The input is used to provide selection functionality.
        // Selection UI is drawn on a canvas under the InkCanvas.
        private void UnprocessedInput_PointerPressed(
          InkUnprocessedInput sender, PointerEventArgs args)
        {
            //inkCanvas.InkPresenter.IsInputEnabled = false;
            if(boundingRect.IsEmpty)
            {
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
        }

        private void UnprocessedInput_PointerMoved(
          InkUnprocessedInput sender, PointerEventArgs args)
        {
            if (!selecting && (boundingRect.Contains(args.CurrentPoint.RawPosition) || movedSelection))
            {
                if (!movedSelection)
                {
                    movedSelection = true;
                    dragPoint = args.CurrentPoint.RawPosition;
                    return;
                }

                Point pos = args.CurrentPoint.RawPosition;
                pos.X -= dragPoint.X;
                pos.Y -= dragPoint.Y;

                boundingRect = inkCanvas.InkPresenter.StrokeContainer.MoveSelected(pos);
                dragPoint = args.CurrentPoint.RawPosition;

                DrawBoundingRect(true);
                movedSelection = true;
            }
            else if(selecting)
            {
                // Add a point to the lasso Polyline object.
                lasso.Points.Add(args.CurrentPoint.RawPosition);
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
            }
            else if (movedSelection)
            {
                saveInk();
                DrawBoundingRect(false);
            }
            selecting = false;
            movedSelection = false;
        }

        // Draw a bounding rectangle, on the selection canvas, encompassing
        // all ink strokes within the lasso area.
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
                    canvasContainer.Children.Add(selectionDeleteButton);
                }
                else
                {
                    canvasContainer.Children.Remove(selectionDeleteButton);
                }

                selectionCanvas.Children.Add(rectangle);
            }
        }

        // Handle new ink or erase strokes to clean up selection UI.
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

        // Clean up selection UI.
        private void ClearSelection()
        {
            var strokes = inkCanvas.InkPresenter.StrokeContainer.GetStrokes();
            foreach (var stroke in strokes)
            {
                stroke.Selected = false;
            }
            ClearDrawnBoundingRect();
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
            saveInk();
        }

        private void buttonToggleTheme(object sender, RoutedEventArgs e)
        {
            darkTheme = !darkTheme;
            ApplicationData.Current.LocalSettings.Values["theme"] = darkTheme.ToString();
            updateTheme();
        }

        private void updateTheme()
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

        private void toggleNotesPanel(object sender, RoutedEventArgs e)
        {
            if (notesPanel.Visibility == Visibility.Visible)
            {
                notesPanel.Visibility = Visibility.Collapsed;
                buttonNotesShow.Visibility = Visibility.Visible;
                buttonNotesHide.Visibility = Visibility.Collapsed;
            }
            else
            {
                notesPanel.Visibility = Visibility.Visible;   
                buttonNotesShow.Visibility = Visibility.Collapsed;
                buttonNotesHide.Visibility = Visibility.Visible;
            }
        }

        private async void noteSelected(object sender, RoutedEventArgs e)
        {
            string path = files[notesList.SelectedIndex];
            StorageFile file = await ApplicationData.Current.LocalFolder.GetFileAsync(System.IO.Path.GetFileName(path));
            canvasScroll.ChangeView(0, 0, 1.0f);
            await loadInk(file);
        }
        
        private void strokeCollected(InkStrokesCollectedEventArgs e)
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
            //buttonInsert.Margin = new Thickness(-buttonInsert.Width, e.Strokes[0].BoundingRect.Y, 0, 0);
        }

        private void strokeErased(InkStrokesErasedEventArgs e)
        {
            inkStack.Push(e.Strokes, true);
        }

        private void buttonUndo_Click(object sender, RoutedEventArgs e)
        {
            if(inkStack.UndoCount > 0)
            {
                inkStack.Undo(inkCanvas.InkPresenter);
                saveInk();
            }
        }

        private void buttonRedo_Click(object sender, RoutedEventArgs e)
        {
            if(inkStack.RedoCount > 0)
            {
                inkStack.Redo(inkCanvas.InkPresenter);
                saveInk();
            }
        }

        private void drawRuledLines()
        {
            canvasContainer.Children.Clear();
            for(int i = 35; i < inkCanvas.Height; i += 35)
            {
                Line line = new Line();
                line.X1 = 10;
                line.Y1 = i;
                line.X2 = inkCanvas.Width - 10;
                line.Y2 = i;
                line.Stroke = new SolidColorBrush(Color.FromArgb(255, 200, 200, 200));
                line.StrokeThickness = 0.5;
                canvasContainer.Children.Add(line);
            }
            
            canvasContainer.Children.Add(selectionCanvas);
            canvasContainer.Children.Add(inkCanvas);
            canvasContainer.Children.Add(buttonInsert);
        }

        private void drawGridLines()
        {
            canvasContainer.Children.Clear();
            for(int x = 0; x < inkCanvas.Width; x += 35)
            {
                Line line = new Line();
                line.X1 = x;
                line.Y1 = 0;
                line.X2 = x;
                line.Y2 = inkCanvas.Height;
                line.Stroke = new SolidColorBrush(Color.FromArgb(255, 200, 200, 200));
                line.StrokeThickness = 0.5;
                canvasContainer.Children.Add(line);
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
                canvasContainer.Children.Add(line);
            }
            
            canvasContainer.Children.Add(selectionCanvas);
            canvasContainer.Children.Add(inkCanvas);
            canvasContainer.Children.Add(buttonInsert);
        }

        private void canvasTypeComboChanged(object sender, RoutedEventArgs e)
        {
            if (canvasContainer == null) { return; }

            if(canvasTypeCombo.SelectedItem == canvasType_Grid)
            {
                drawGridLines();
            }
            else if(canvasTypeCombo.SelectedItem == canvasType_Ruled)
            {
                drawRuledLines();
            }
            else
            {
                canvasContainer.Children.Clear();
                canvasContainer.Children.Add(selectionCanvas);
                canvasContainer.Children.Add(inkCanvas);
                canvasContainer.Children.Add(buttonInsert);       
            }

            if(sender != null) saveCanvasType();
        }
        
        private void resizeCanvas(object sender, RoutedEventArgs e)
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
            inkCanvas.Width = width;
            inkCanvas.Height = height;
            canvasTypeComboChanged(null, null);

            //if (sender != null) saveCanvasSize();
        }

        private void buttonCenterAlign_ClickAsync(object sender, RoutedEventArgs e)
        {
            canvasContainer.HorizontalAlignment = HorizontalAlignment.Center;
        }
        private void buttonRightAlign_ClickAsync(object sender, RoutedEventArgs e)
        {
            canvasContainer.HorizontalAlignment = HorizontalAlignment.Right;
        }
        private void buttonLeftAlign_ClickAsync(object sender, RoutedEventArgs e)
        {
            canvasContainer.HorizontalAlignment = HorizontalAlignment.Left;
        }

        private void buttonZoomReset_Click(object sender, RoutedEventArgs e)
        {
            canvasScroll.ChangeView(canvasScroll.HorizontalOffset, canvasScroll.VerticalOffset, 1.0f);
        }

        private void myScrollViewer_PointerPressed(object sender, PointerRoutedEventArgs e)
        {
            if (e.Pointer.PointerDeviceType == Windows.Devices.Input.PointerDeviceType.Pen)
            {
                (canvasScroll.Content as UIElement).ManipulationMode &= ~ManipulationModes.System;
            }
        }

        private void myScrollViewer_PointerReleased(object sender, PointerRoutedEventArgs e)
        {
            if (e.Pointer.PointerDeviceType == Windows.Devices.Input.PointerDeviceType.Pen)
            {
                (canvasScroll.Content as UIElement).ManipulationMode |= ManipulationModes.System;
            }
        }

        private void myScrollViewer_PointerCanceled(object sender, PointerRoutedEventArgs e)
        {
            if (e.Pointer.PointerDeviceType == Windows.Devices.Input.PointerDeviceType.Pen)
            {
                (canvasScroll.Content as UIElement).ManipulationMode |= ManipulationModes.System;
            }
        }

        private async void buttonSave_ClickAsync(object sender, RoutedEventArgs e)
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

        private async void buttonLoad_ClickAsync(object sender, RoutedEventArgs e)
        {
            // Use a file picker to identify ink file.
            Windows.Storage.Pickers.FileOpenPicker openPicker =
                new Windows.Storage.Pickers.FileOpenPicker();
            openPicker.SuggestedStartLocation =
                Windows.Storage.Pickers.PickerLocationId.DocumentsLibrary;
            openPicker.FileTypeFilter.Add(".note");
            // Show the file picker.
            StorageFile file = await openPicker.PickSingleFileAsync();
            await loadInk(file);
        }

        private async void loadLastInk()
        {
            updateTheme();

            ApplicationDataContainer localSettings = ApplicationData.Current.LocalSettings;
            if (!string.IsNullOrEmpty(localSettings.Values["lastFileToken"] as string))
            {
                try
                {
                    StorageFile file = await StorageApplicationPermissions.FutureAccessList.GetFileAsync(localSettings.Values["lastFileToken"] as string);
                    await loadInk(file);
                }
                catch(Exception ex)
                {
                    localSettings.Values["lastFileToken"] = "";
                }
            }
        }

        private async Task loadInk(StorageFile file)
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
                        canvasTypeComboChanged(null, null);
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

                ApplicationDataContainer localSettings = ApplicationData.Current.LocalSettings;
                string faToken = localSettings.Values["lastFileToken"] as string;
                if (!string.IsNullOrEmpty(faToken))
                {
                    StorageApplicationPermissions.FutureAccessList.AddOrReplace(faToken, file);
                }
                else
                {
                    localSettings.Values["lastFileToken"] = StorageApplicationPermissions.FutureAccessList.Add(file);
                }

                if (!files.Contains(file.Path))
                {
                    await addFile(file.Path, true);
                }
            }
        }

        private void inkCleared(InkToolbar toolbar, object sender) { saveInk(); }

        private async void saveCanvasType()
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

        private async void saveCanvasSize()
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

        private async void saveInk()
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

            saveIcon.Visibility = Visibility.Collapsed;
            saving = false;
        }
    }
}