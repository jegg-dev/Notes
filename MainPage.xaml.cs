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

namespace Notes
{
    public sealed partial class MainPage : Page
    {
        private bool saving = false;

        private List<string> files;

        private InkActionStack inkStack = new InkActionStack(100);

        private bool darkTheme;

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

            readFiles();

            inkCanvas.InkPresenter.StrokesCollected += (i, e) => { strokeCollected(e); saveInk(); };
            inkCanvas.InkPresenter.StrokesErased += (i, e) => { strokeErased(e); saveInk(); };
            inkCanvas.Loaded += inkLoaded;
            inkToolbar.Loaded += inkLoaded;
        }

        /*protected override void OnDoubleTapped(DoubleTappedRoutedEventArgs e)
        {
            if(e.PointerDeviceType == Windows.Devices.Input.PointerDeviceType.Touch)
            {
                buttonZoomReset_Click(null, null);
            }
            base.OnDoubleTapped(e);
        }*/

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
                string backgroundCode = "1B1B1B";
                Color backgroundColor = Tools.GetColorFromHexcode(backgroundCode);

                foreach (InkStroke stroke in inkCanvas.InkPresenter.StrokeContainer.GetStrokes())
                {
                    if (stroke.DrawingAttributes.Color == Colors.Black)
                    {
                        InkDrawingAttributes da = stroke.DrawingAttributes;
                        da.Color = Colors.White;
                        stroke.DrawingAttributes = da;
                    }
                }
                //canvasContainer.Background = new SolidColorBrush(Colors.Black);
                RequestedTheme = ElementTheme.Dark;
                //drawingCanvas.Background = new SolidColorBrush(backgroundColor);
                ((SolidColorBrush)Application.Current.Resources["DarkerBackground"]).Color = backgroundColor;

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
                //canvasContainer.Background = new SolidColorBrush(Colors.White);
                RequestedTheme = ElementTheme.Light;
                //drawingCanvas.Background = new SolidColorBrush(Colors.LightGray);
                ((SolidColorBrush)Application.Current.Resources["DarkerBackground"]).Color = Colors.LightGray;

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
            inkStack.Push(e.Strokes, false);
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
                line.Stroke = new SolidColorBrush(Windows.UI.ColorHelper.FromArgb(255, 200, 200, 200));
                line.StrokeThickness = 0.5;
                canvasContainer.Children.Add(line);
            }
            canvasContainer.Children.Add(inkCanvas);
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
                line.Stroke = new SolidColorBrush(Windows.UI.ColorHelper.FromArgb(255, 200, 200, 200));
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
                line.Stroke = new SolidColorBrush(Windows.UI.ColorHelper.FromArgb(255, 200, 200, 200));
                line.StrokeThickness = 0.5;
                canvasContainer.Children.Add(line);
            }
            canvasContainer.Children.Add(inkCanvas);
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
                canvasContainer.Children.Add(inkCanvas);
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
            ApplicationDataContainer localSettings = ApplicationData.Current.LocalSettings;
            if (!string.IsNullOrEmpty(localSettings.Values["lastFileToken"] as string))
            {
                StorageFile file = await StorageApplicationPermissions.FutureAccessList.GetFileAsync(localSettings.Values["lastFileToken"] as string);
                await loadInk(file);
                updateTheme();
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