namespace AxialFanMVC.Services
{
    // IMPORTANT PLATFORM CAVEAT: ActiViz.NET ships native Windows VTK
    // binaries built for .NET Framework/WinForms interop. It does NOT run
    // on Linux, and won't load in a Linux-hosted ASP.NET Core process
    // (typical for MVC apps in containers/Kestrel-on-Linux). This class
    // only works if this web app is hosted on Windows (IIS or Kestrel on
    // Windows Server) with the ActiViz.NET native DLLs deployed alongside it.
    // If you deploy on Linux, replace this class with a call to a small
    // Python/PyVista sidecar service instead — the same shape as the
    // existing OptimizationBackgroundService -> FastAPI optimizer call in
    // this codebase (see OptimizerService:BaseUrl in appsettings.json).
    public static class CfdVtkRenderer
    {
        public static (string PngPath, string VtpPath) RenderOffscreen(string casePath, string outputDir)
        {
            Directory.CreateDirectory(outputDir);

            string dummyFoamFile = Path.Combine(casePath, "case.foam");
            if (!File.Exists(dummyFoamFile)) File.WriteAllText(dummyFoamFile, "");

            var reader = vtkOpenFOAMReader.New();
            reader.SetFileName(dummyFoamFile);
            reader.CreateCellToPointOn();
            reader.Update();
            reader.UpdateTimeInformation();

            var timeValues = reader.GetTimeValues();
            int lastIdx = timeValues.GetNumberOfTuples() - 1;
            if (lastIdx >= 0)
            {
                reader.SetTimeValue(timeValues.GetTuple1(lastIdx));
                reader.Update();
            }

            var merge = vtkMergeBlocks.New();
            merge.SetInputConnection(reader.GetOutputPort());
            merge.Update();

            var plane = vtkPlane.New();
            plane.SetOrigin(0, 0, 0);
            plane.SetNormal(0, 1, 0);

            var cutter = vtkCutter.New();
            cutter.SetCutFunction(plane);
            cutter.SetInputConnection(merge.GetOutputPort());
            cutter.Update();

            var pressureArray = cutter.GetOutput().GetPointData().GetArray("p");
            double[] range = pressureArray != null ? pressureArray.GetRange() : new double[] { 0, 1 };

            var lut = vtkLookupTable.New();
            lut.SetHueRange(0.667, 0.0);
            lut.SetTableRange(range[0], range[1]);
            lut.Build();

            var mapper = vtkPolyDataMapper.New();
            mapper.SetInputConnection(cutter.GetOutputPort());
            mapper.SetScalarModeToUsePointFieldData();
            mapper.SelectColorArray("p");
            mapper.SetLookupTable(lut);
            mapper.SetScalarRange(range[0], range[1]);
            mapper.ScalarVisibilityOn();

            var actor = vtkActor.New();
            actor.SetMapper(mapper);

            var scalarBar = vtkScalarBarActor.New();
            scalarBar.SetLookupTable(lut);
            scalarBar.SetTitle("Static Pressure (Pa)");

            // No on-screen window on a server — render straight to an
            // off-screen buffer. This is the key difference from the
            // desktop RenderWindowControl version.
            var renderer = vtkRenderer.New();
            renderer.AddActor(actor);
            renderer.AddActor2D(scalarBar);
            renderer.SetBackground(0.15, 0.15, 0.18);
            renderer.ResetCamera();

            var renderWindow = vtkRenderWindow.New();
            renderWindow.SetOffScreenRendering(1);
            renderWindow.AddRenderer(renderer);
            renderWindow.SetSize(1600, 1000);
            renderWindow.Render();

            string pngPath = Path.Combine(outputDir, "pressure_slice.png");
            var w2i = vtkWindowToImageFilter.New();
            w2i.SetInput(renderWindow);
            w2i.SetInputBufferTypeToRGBA();
            w2i.ReadFrontBufferOff();
            w2i.Update();

            var pngWriter = vtkPNGWriter.New();
            pngWriter.SetFileName(pngPath);
            pngWriter.SetInputConnection(w2i.GetOutputPort());
            pngWriter.Write();

            string vtpPath = Path.Combine(outputDir, "pressure_slice.vtp");
            var vtpWriter = vtkXMLPolyDataWriter.New();
            vtpWriter.SetFileName(vtpPath);
            vtpWriter.SetInputConnection(cutter.GetOutputPort());
            vtpWriter.Write();

            return (pngPath, vtpPath);
        }
    }
}
