using System.Runtime.InteropServices;
using Windows.Graphics.Capture;
using Windows.Graphics.DirectX;
using Windows.Graphics.DirectX.Direct3D11;
using Windows.Graphics.Imaging;
using Windows.Security.Cryptography;
using WinRT;
using SMAP_WPF.Core;

namespace SMAP_WPF;

public static class WindowCapture
{
    [ComImport, Guid("3628e81b-3cac-4c60-b7f4-23ce0e0c3356"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    interface IItemInterop
    {
        IntPtr CreateForWindow(IntPtr window, ref Guid iid);
        IntPtr CreateForMonitor(IntPtr monitor, ref Guid iid);
    }
    [DllImport("combase.dll", CharSet = CharSet.Unicode)] static extern int WindowsCreateString(string text, int length, out IntPtr value);
    [DllImport("combase.dll")] static extern int WindowsDeleteString(IntPtr value);
    [DllImport("combase.dll")] static extern int RoGetActivationFactory(IntPtr name, ref Guid iid, out IntPtr factory);
    [DllImport("d3d11.dll")] static extern int D3D11CreateDevice(IntPtr adapter, int driver, IntPtr software, uint flags,
        IntPtr levels, uint count, uint sdk, out IntPtr device, out int level, out IntPtr context);
    [DllImport("d3d11.dll")] static extern int CreateDirect3D11DeviceFromDXGIDevice(IntPtr dxgi, out IntPtr device);
    static GraphicsCaptureItem Item(IntPtr window)
    {
        const string name = "Windows.Graphics.Capture.GraphicsCaptureItem";
        IntPtr hstring = IntPtr.Zero, factory = IntPtr.Zero, item = IntPtr.Zero; object? interop = null;
        try
        {
            Marshal.ThrowExceptionForHR(WindowsCreateString(name, name.Length, out hstring));
            var iid = typeof(IItemInterop).GUID; Marshal.ThrowExceptionForHR(RoGetActivationFactory(hstring, ref iid, out factory));
            interop = Marshal.GetObjectForIUnknown(factory);
            var itemIid = new Guid("79c3f95b-31f7-4ec2-a464-632ef5d30760");
            item = ((IItemInterop)interop).CreateForWindow(window, ref itemIid);
            return MarshalInterface<GraphicsCaptureItem>.FromAbi(item);
        }
        finally
        {
            if (item != IntPtr.Zero) Marshal.Release(item);
            if (interop != null && Marshal.IsComObject(interop)) Marshal.ReleaseComObject(interop);
            if (factory != IntPtr.Zero) Marshal.Release(factory);
            if (hstring != IntPtr.Zero) WindowsDeleteString(hstring);
        }
    }
    static IDirect3DDevice Device()
    {
        IntPtr d3d = IntPtr.Zero, context = IntPtr.Zero, dxgi = IntPtr.Zero, projected = IntPtr.Zero;
        try
        {
            int hr = D3D11CreateDevice(IntPtr.Zero, 1, IntPtr.Zero, 0x20, IntPtr.Zero, 0, 7, out d3d, out _, out context);
            if (hr < 0) Marshal.ThrowExceptionForHR(hr);
            var iid = new Guid("54ec77fa-1377-44e6-8c32-88fd5f44c84c");
            Marshal.ThrowExceptionForHR(Marshal.QueryInterface(d3d, in iid, out dxgi));
            Marshal.ThrowExceptionForHR(CreateDirect3D11DeviceFromDXGIDevice(dxgi, out projected));
            return MarshalInterface<IDirect3DDevice>.FromAbi(projected);
        }
        finally
        {
            foreach (var ptr in new[] { projected, dxgi, context, d3d }) if (ptr != IntPtr.Zero) Marshal.Release(ptr);
        }
    }
    public static Task<CapturePixels> OnceAsync(IntPtr window, CancellationToken cancellationToken = default)
    {
        FeatureAvailability.RequireSheetMapping();
        return CaptureAsync(window,cancellationToken);
    }
    static async Task<CapturePixels> CaptureAsync(IntPtr window,CancellationToken cancellationToken)
    {
        if (!GraphicsCaptureSession.IsSupported()) throw new NotSupportedException("WindowsGraphicsCaptureUnavailable");
        var item = Item(window); var size = item.Size;
        if (size.Width <= 0 || size.Height <= 0 || (long)size.Width * size.Height > 16_000_000) throw new InvalidOperationException("CaptureSizeUnsupported");
        using var device = Device();
        using var pool = Direct3D11CaptureFramePool.CreateFreeThreaded(device, DirectXPixelFormat.B8G8R8A8UIntNormalized, 2, size);
        using var capture = pool.CreateCaptureSession(item);
        if (OperatingSystem.IsWindowsVersionAtLeast(10, 0, 19041)) capture.IsCursorCaptureEnabled = false;
        var ready = new TaskCompletionSource<Direct3D11CaptureFrame>(TaskCreationOptions.RunContinuationsAsynchronously);
        var gate = new object(); bool closed = false;
        void Arrived(Direct3D11CaptureFramePool sender, object args)
        {
            try { var frame = sender.TryGetNextFrame(); lock (gate) { if (frame != null && (closed || !ready.TrySetResult(frame))) frame.Dispose(); } }
            catch (Exception error) { ready.TrySetException(error); }
        }
        pool.FrameArrived += Arrived;
        try
        {
            capture.StartCapture();
            var frame = await ready.Task.WaitAsync(TimeSpan.FromSeconds(3), cancellationToken);
            // 在回调之外异步复制，避免阻塞捕获线程；不写磁盘，不上传。
            using var bitmap = await SoftwareBitmap.CreateCopyFromSurfaceAsync(frame.Surface, BitmapAlphaMode.Ignore).AsTask(cancellationToken);
            var buffer = new Windows.Storage.Streams.Buffer(checked((uint)(bitmap.PixelWidth * bitmap.PixelHeight * 4)));
            bitmap.CopyToBuffer(buffer); CryptographicBuffer.CopyToByteArray(buffer, out var bytes);
            return new(bytes, Math.Min(bitmap.PixelWidth, frame.ContentSize.Width), Math.Min(bitmap.PixelHeight, frame.ContentSize.Height), bitmap.PixelWidth * 4);
        }
        finally
        {
            pool.FrameArrived -= Arrived;
            lock (gate) { closed = true; if (ready.Task.IsCompletedSuccessfully) ready.Task.Result.Dispose(); }
        }
    }
}
