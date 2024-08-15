using System.Runtime.InteropServices;
using System.Runtime.InteropServices.ComTypes;

namespace GitHub.secile.Video
{
    static class DirectShow
    {
        #region Function

        /// <summary>フィルタの一覧を取得する。</summary>
        public static List<string> GetFiltes(Guid category)
        {
            var result = new List<string>();

            EnumMonikers(category, (moniker, prop) =>
            {
                object value = null;
                prop.Read("FriendlyName", ref value, 0);
                var name = (string)value;

                result.Add(name);

                return false; // 継続。
            });

            return result;
        }

        /// <summary>モニカを列挙する。</summary>
        /// <remarks>モニカとはCOMオブジェクトを識別する別名のこと。</remarks>
        private static void EnumMonikers(Guid category, Func<IMoniker, IPropertyBag, bool> func)
        {
            IEnumMoniker enumerator = null;
            ICreateDevEnum device = null;

            try
            {
                // ICreateDevEnum インターフェース取得.
                device = (ICreateDevEnum)Activator.CreateInstance(Type.GetTypeFromCLSID(DsGuid.CLSID_SystemDeviceEnum));

                // IEnumMonikerの作成.
                device.CreateClassEnumerator(ref category, ref enumerator, 0);

                // 列挙可能なデバイスが存在しない場合null
                if (enumerator == null) return;

                // 列挙.
                var monikers = new IMoniker[1];
                var fetched = IntPtr.Zero;

                while (enumerator.Next(monikers.Length, monikers, fetched) == 0)
                {
                    var moniker = monikers[0];

                    // プロパティバッグへのバインド.
                    object value = null;
                    Guid guid = DsGuid.IID_IPropertyBag;
                    moniker.BindToStorage(null, null, ref guid, out value);
                    var prop = (IPropertyBag)value;

                    try
                    {
                        // trueで列挙完了。falseで継続する。
                        var rc = func(moniker, prop);
                        if (rc == true) break;
                    }
                    finally
                    {
                        // プロパティバッグの解放
                        Marshal.ReleaseComObject(prop);

                        // 列挙したモニカの解放.
                        if (moniker != null) Marshal.ReleaseComObject(moniker);
                    }
                }
            }
            finally
            {
                if (enumerator != null) Marshal.ReleaseComObject(enumerator);
                if (device != null) Marshal.ReleaseComObject(device);
            }
        }

        #endregion


        #region Interface

        [ComVisible(true), ComImport(), Guid("29840822-5B84-11D0-BD3B-00A0C911CE86"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
        public interface ICreateDevEnum
        {
            int CreateClassEnumerator([In] ref Guid pType, [In, Out] ref System.Runtime.InteropServices.ComTypes.IEnumMoniker ppEnumMoniker, [In] int dwFlags);
        }

        [ComVisible(true), ComImport(), Guid("55272A00-42CB-11CE-8135-00AA004BB851"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
        public interface IPropertyBag
        {
            int Read([MarshalAs(UnmanagedType.LPWStr)] string PropName, ref object Var, int ErrorLog);
            int Write(string PropName, ref object Var);
        }

        #endregion

        #region Guid

        public static class DsGuid
        {
            // CLSID
            public static readonly Guid CLSID_SystemDeviceEnum = new("{62BE5D10-60EB-11d0-BD3B-00A0C911CE86}");
            public static readonly Guid CLSID_VideoInputDeviceCategory = new("{860BB310-5D01-11d0-BD3B-00A0C911CE86}");

            public static readonly Guid IID_IPropertyBag = new("{55272A00-42CB-11CE-8135-00AA004BB851}");
        }
        #endregion
    }
}
