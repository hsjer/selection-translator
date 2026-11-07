using System;
using System.Collections.Generic;
using System.Windows.Forms;

namespace SelectionTranslator
{
    internal sealed class HotkeyWindow : NativeWindow, IDisposable
    {
        private readonly object _sync = new object();
        private readonly HashSet<int> _registered = new HashSet<int>();

        public event EventHandler<int> HotkeyPressed;

        public HotkeyWindow()
        {
            CreateParams parameters = new CreateParams();
            parameters.Parent = new IntPtr(-3); // HWND_MESSAGE
            CreateHandle(parameters);
        }

        public bool Register(int id, int modifiers, Keys key)
        {
            lock (_sync)
            {
                if (_registered.Contains(id))
                {
                    return true;
                }
            }

            bool ok = NativeMethods.RegisterHotKey(
                Handle,
                id,
                modifiers,
                (int)key);

            if (ok)
            {
                lock (_sync)
                {
                    _registered.Add(id);
                }
            }

            return ok;
        }

        public void Unregister(int id)
        {
            lock (_sync)
            {
                if (!_registered.Contains(id))
                {
                    return;
                }
                _registered.Remove(id);
            }

            NativeMethods.UnregisterHotKey(Handle, id);
        }

        public void UnregisterAll()
        {
            int[] ids;
            lock (_sync)
            {
                ids = new int[_registered.Count];
                _registered.CopyTo(ids, 0);
                _registered.Clear();
            }

            foreach (int id in ids)
            {
                NativeMethods.UnregisterHotKey(Handle, id);
            }
        }

        protected override void WndProc(ref Message message)
        {
            if (message.Msg == NativeMethods.WM_HOTKEY)
            {
                int id = 0;
                try
                {
                    id = (int)message.WParam.ToInt32();
                }
                catch
                {
                    id = 0;
                }

                EventHandler<int> handler = HotkeyPressed;
                if (handler != null)
                {
                    try
                    {
                        handler(this, id);
                    }
                    catch (Exception ex)
                    {
                        Log.Error("处理全局快捷键失败", ex);
                    }
                }
            }

            base.WndProc(ref message);
        }

        public void Dispose()
        {
            UnregisterAll();
            if (Handle != IntPtr.Zero)
            {
                DestroyHandle();
            }
        }
    }
}
