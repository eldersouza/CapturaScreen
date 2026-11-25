using System;
using Captura.Windows.MediaFoundation;
using Captura.Video;
using SharpDX;
using SharpDX.Direct2D1;
using SharpDX.Direct3D;
using SharpDX.Direct3D11;
using SharpDX.DXGI;
using SharpDX.Mathematics.Interop;
using SharpDX.WIC;
using AlphaMode = SharpDX.Direct2D1.AlphaMode;
using Device = SharpDX.Direct3D11.Device;
using Factory = SharpDX.DirectWrite.Factory;
using Factory1 = SharpDX.Direct2D1.Factory1;
using PixelFormat = SharpDX.Direct2D1.PixelFormat;

namespace Captura.Windows.DirectX
{
    public class Direct2DEditorSession : IDisposable
    {
        readonly IPreviewWindow _previewWindow;
        readonly int _width;
        readonly int _height;

        public Texture2D DesktopTexture { get; private set; }

        public Device Device { get; private set; }
        public Texture2D StagingTexture { get; private set; }
        public RenderTarget RenderTarget { get; private set; }
        public Texture2D PreviewTexture { get; private set; }

        SolidColorBrush _solidColorBrush;
        readonly Factory1 _factory;
        Factory _writeFactory;
        ImagingFactory _imagingFactory;

        public Factory WriteFactory => _writeFactory ??= new Factory();

        public ImagingFactory ImagingFactory => _imagingFactory ??= new ImagingFactory();

        public Lazy<MfColorConverter> ColorConverter { get; private set; }

        public event Action ResourceRecreated;

        public SolidColorBrush GetSolidColorBrush(RawColor4 Color)
        {
            if (_solidColorBrush == null)
            {
                _solidColorBrush = new SolidColorBrush(RenderTarget, Color);
            }
            else _solidColorBrush.Color = Color;

            return _solidColorBrush;
        }

        public Direct2DEditorSession(int Width, int Height, IPreviewWindow PreviewWindow)
        {
            _width = Width;
            _height = Height;
            _previewWindow = PreviewWindow;
            _factory = new Factory1(FactoryType.MultiThreaded);

            RecreateDeviceAndTarget();
        }

        void RecreateDeviceAndTarget()
        {
            var oldRenderTarget = RenderTarget;
            var oldDesktopTexture = DesktopTexture;
            var oldPreviewTexture = PreviewTexture;
            var oldStagingTexture = StagingTexture;
            var oldDevice = Device;

            RenderTarget = null;
            PreviewTexture = null;
            DesktopTexture = null;
            StagingTexture = null;
            Device = null;

            _solidColorBrush?.Dispose();
            _solidColorBrush = null;

            if (ColorConverter != null && ColorConverter.IsValueCreated)
            {
                ColorConverter.Value.Dispose();
                ColorConverter = null;
            }

            oldRenderTarget?.Dispose();
            oldPreviewTexture?.Dispose();
            oldDesktopTexture?.Dispose();
            oldStagingTexture?.Dispose();
            oldDevice?.Dispose();

            Device = new Device(DriverType.Hardware,
                DeviceCreationFlags.BgraSupport | DeviceCreationFlags.VideoSupport);

            StagingTexture = new Texture2D(Device, new Texture2DDescription
            {
                CpuAccessFlags = CpuAccessFlags.Read,
                BindFlags = BindFlags.None,
                Format = Format.B8G8R8A8_UNorm,
                Width = _width,
                Height = _height,
                OptionFlags = ResourceOptionFlags.None,
                MipLevels = 1,
                ArraySize = 1,
                SampleDescription = { Count = 1, Quality = 0 },
                Usage = ResourceUsage.Staging
            });

            DesktopTexture = new Texture2D(Device, new Texture2DDescription
            {
                CpuAccessFlags = CpuAccessFlags.None,
                BindFlags = BindFlags.RenderTarget | BindFlags.ShaderResource,
                Format = Format.B8G8R8A8_UNorm,
                Width = _width,
                Height = _height,
                OptionFlags = ResourceOptionFlags.None,
                MipLevels = 1,
                ArraySize = 1,
                SampleDescription = { Count = 1, Quality = 0 },
                Usage = ResourceUsage.Default
            });

            var desc = DesktopTexture.Description;
            desc.OptionFlags = ResourceOptionFlags.Shared;

            PreviewTexture = new Texture2D(Device, desc);

            var pixelFormat = new PixelFormat(Format.Unknown, AlphaMode.Ignore);
            var renderTargetProps = new RenderTargetProperties(pixelFormat);

            using (var surface = DesktopTexture.QueryInterface<Surface>())
            {
                RenderTarget = new RenderTarget(_factory, surface, renderTargetProps);
            }

            ResourceRecreated?.Invoke();
            ColorConverter = new Lazy<MfColorConverter>(() => new MfColorConverter(_width, _height, Device));
        }

        public Texture2D CreateGdiTexture(int Width, int Height)
        {
            var desc = new Texture2DDescription
            {
                Width = Width,
                Height = Height,
                ArraySize = 1,
                Format = Format.B8G8R8A8_UNorm,
                Usage = ResourceUsage.Default,
                BindFlags = BindFlags.ShaderResource | BindFlags.RenderTarget,
                MipLevels = 1,
                SampleDescription = { Count = 1 },
                OptionFlags = ResourceOptionFlags.GdiCompatible
            };

            return new Texture2D(Device, desc);
        }

        void CreateRenderTarget()
        {
            if (DesktopTexture == null || DesktopTexture.IsDisposed)
                throw new ObjectDisposedException(nameof(Direct2DEditorSession));

            if (RenderTarget != null)
            {
                RenderTarget.Dispose();
                RenderTarget = null;
            }

            _solidColorBrush?.Dispose();
            _solidColorBrush = null;

            var pixelFormat = new PixelFormat(Format.Unknown, AlphaMode.Ignore);
            var renderTargetProps = new RenderTargetProperties(pixelFormat);

            using (var surface = DesktopTexture.QueryInterface<Surface>())
            {
                RenderTarget = new RenderTarget(_factory, surface, renderTargetProps);
            }

            ResourceRecreated?.Invoke();
        }

        public void RecreateRenderTarget()
        {
            try
            {
                RecreateDeviceAndTarget();
                Device.ImmediateContext.Flush();
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Direct2DEditorSession.RecreateRenderTarget failed: {ex}");
            }
        }

        public bool BeginDraw()
        {
            try
            {
                if (RenderTarget == null)
                    CreateRenderTarget();

                RenderTarget.BeginDraw();
                return true;
            }
            catch (SharpDXException Ex) when (Ex.ResultCode == SharpDX.Direct2D1.ResultCode.RecreateTarget ||
                                             Ex.ResultCode == SharpDX.Direct2D1.ResultCode.WrongResourceDomain ||
                                             Ex.ResultCode == SharpDX.Direct2D1.ResultCode.WrongFactory ||
                                             Ex.ResultCode == SharpDX.DXGI.ResultCode.DeviceRemoved ||
                                             Ex.ResultCode == SharpDX.DXGI.ResultCode.DeviceReset)
            {
                RecreateRenderTarget();

                if (RenderTarget == null || Device == null)
                    return false;

                RenderTarget.BeginDraw();
                return true;
            }
        }

        public bool IsPreviewVisible => _previewWindow?.IsVisible == true;

        public bool EndDraw()
        {
            try
            {
                RenderTarget.EndDraw();
            }
            catch (SharpDXException Ex) when (Ex.ResultCode == SharpDX.Direct2D1.ResultCode.RecreateTarget ||
                                             Ex.ResultCode == SharpDX.Direct2D1.ResultCode.WrongResourceDomain ||
                                             Ex.ResultCode == SharpDX.Direct2D1.ResultCode.WrongFactory ||
                                             Ex.ResultCode == SharpDX.DXGI.ResultCode.DeviceRemoved ||
                                             Ex.ResultCode == SharpDX.DXGI.ResultCode.DeviceReset)
            {
                RecreateRenderTarget();
                return false;
            }

            if (DesktopTexture == null || StagingTexture == null || Device == null)
                return false;

            Device.ImmediateContext.CopyResource(DesktopTexture, StagingTexture);

            if (_previewWindow.IsVisible)
            {
                Device.ImmediateContext.CopyResource(StagingTexture, PreviewTexture);
            }

            Device.ImmediateContext.Flush();
            return true;
        }

        public void Dispose()
        {
            if (ColorConverter != null && ColorConverter.IsValueCreated)
            {
                ColorConverter.Value.Dispose();
            }

            _solidColorBrush?.Dispose();
            _solidColorBrush = null;

            RenderTarget?.Dispose();
            RenderTarget = null;

            _factory.Dispose();

            _writeFactory?.Dispose();
            _writeFactory = null;

            _imagingFactory?.Dispose();
            _imagingFactory = null;

            PreviewTexture?.Dispose();
            PreviewTexture = null;

            DesktopTexture?.Dispose();
            DesktopTexture = null;

            StagingTexture?.Dispose();
            StagingTexture = null;

            Device?.Dispose();
            Device = null;
        }
    }
}