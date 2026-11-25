using System;
using System.Threading.Tasks;
using SharpDX;
using SharpDX.DXGI;

namespace Captura.Windows.DesktopDuplication
{
    public class FrameGrabber : IDisposable
    {
        readonly OutputDuplication _deskDupl;

        public FrameGrabber(OutputDuplication DeskDupl)
        {
            _deskDupl = DeskDupl;
        }

        public AcquireResult Grab()
        {
            try
            {
                const int timeout = 50;

                var result = _deskDupl.TryAcquireNextFrame(timeout, out var frameInfo, out var desktopResource);

                if (result == ResultCode.WaitTimeout)
                    return null;

                return new AcquireResult(result, frameInfo, desktopResource);
            }
            catch
            {
                return new AcquireResult(Result.Fail);
            }
        }

        public void Release()
        {
            try
            {
                _deskDupl.ReleaseFrame();
            }
            catch { }
        }

        public void Dispose()
        {
        }
    }
}