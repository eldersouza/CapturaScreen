using System;
using System.Runtime.InteropServices;
using System.Threading.Tasks;
using Captura.Windows.MediaFoundation;
using Captura.Native;
using SharpDX;
using SharpDX.Direct3D11;
using SharpDX.DXGI;
using Device = SharpDX.Direct3D11.Device;
using MapFlags = SharpDX.Direct3D11.MapFlags;

namespace Captura.Windows.DirectX
{
    public class Texture2DFrame : INV12Frame
    {
        public Texture2D Texture { get; }
        public Texture2D PreviewTexture { get; }

        public Device Device { get; }

        public TimeSpan Timestamp { get; }

        Lazy<MfColorConverter> _colorConverter;

        public Texture2DFrame(Texture2D Texture,
            Device Device,
            Texture2D PreviewTexture,
            TimeSpan Timestamp,
            Lazy<MfColorConverter> ColorConverter)
        {
            if (Texture == null)
                throw new InvalidOperationException("The Direct2D staging texture is unavailable.");

            if (Device == null)
                throw new InvalidOperationException("The Direct3D device is unavailable.");

            _colorConverter = ColorConverter;
            this.Timestamp = Timestamp;
            this.Texture = Texture;
            this.Device = Device;
            this.PreviewTexture = PreviewTexture;

            var desc = Texture.Description;

            Width = desc.Width;
            Height = desc.Height;
        }

        Texture2DFrame() { }

        public static IBitmapFrame DummyFrame { get; } = new Texture2DFrame();

        public void Dispose() { }

        public int Width { get; }
        public int Height { get; }

        public void CopyTo(byte[] Buffer)
        {
            var mapSource = Device.ImmediateContext.MapSubresource(Texture, 0, MapMode.Read, MapFlags.None);

            var destStride = Width * 4;

            try
            {
                // Do not copy directly, strides may be different
                Parallel.For(0, Height, Y =>
                {
                    Marshal.Copy(mapSource.DataPointer + Y * mapSource.RowPitch,
                        Buffer,
                        Y * destStride,
                        destStride);
                });
            }
            finally
            {
                Device.ImmediateContext.UnmapSubresource(Texture, 0);
            }
        }

        public void CopyTo(IntPtr Buffer)
        {
            var mapSource = Device.ImmediateContext.MapSubresource(Texture, 0, MapMode.Read, MapFlags.None);

            var destStride = Width * 4;

            try
            {
                // Do not copy directly, strides may be different
                Parallel.For(0, Height, Y =>
                {
                    Kernel32.CopyMemory(Buffer + Y * destStride,
                        mapSource.DataPointer + Y * mapSource.RowPitch,
                        destStride);
                });
            }
            finally
            {
                Device.ImmediateContext.UnmapSubresource(Texture, 0);
            }
        }

        public void CopyNV12To(byte[] Buffer)
        {
            if (Buffer == null)
                throw new ArgumentNullException(nameof(Buffer));

            var expected = Width * Height * 3 / 2;
            if (Buffer.Length < expected)
                throw new ArgumentException($"Buffer is too small for NV12 output. Need {expected}, got {Buffer.Length}.", nameof(Buffer));

            if (Texture == null || Texture.IsDisposed || Device == null || Device.IsDisposed)
            {
                Array.Clear(Buffer, 0, Buffer.Length);
                return;
            }

            Array.Clear(Buffer, 0, Buffer.Length);

            try
            {
                if (_colorConverter == null || (_colorConverter.IsValueCreated && _colorConverter.Value == null))
                    throw new InvalidOperationException("The color converter is unavailable.");

                _colorConverter.Value.Convert(Texture, Buffer);
            }
            catch (Exception)
            {
                // Avoid a second MapSubresource fallback here: on Intel GPUs this can crash the process during shutdown.
                // The recorder will keep a valid video file while the final frame is simply black if the device is already torn down.
                Array.Clear(Buffer, 0, Buffer.Length);
            }
        }
    }
}