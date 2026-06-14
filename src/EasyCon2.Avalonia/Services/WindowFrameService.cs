    using System;
    using System.Collections.Generic;
    using System.Runtime.InteropServices;
    using Avalonia;
    using Avalonia.Controls;
    using Avalonia.Platform;
    using Avalonia.Threading;

    namespace EasyCon2.Avalonia.Services;

    internal static class WindowFrameService
    {
        private const double MacOSWindowButtonOriginX = 12;
        private const double MacOSWindowButtonOriginY = 2;
        private const nint NSWindowToolbarStyleUnified = 3;
        private const string MacOSWindowFrameObserverClassName = "EasyConWindowFrameObserver";
        private static readonly string[] s_macOSWindowFrameNotifications =
        [
            "NSWindowDidResizeNotification",
            "NSWindowDidEndLiveResizeNotification",
            "NSWindowDidMoveNotification",
            "NSWindowDidBecomeKeyNotification",
            "NSWindowDidBecomeMainNotification",
            "NSWindowDidEnterFullScreenNotification",
            "NSWindowDidExitFullScreenNotification",
            "NSWindowDidChangeScreenNotification"
        ];
        private static readonly Dictionary<Window, IntPtr> s_macOSNotificationObservers = new();

        public static AppBuilder UsePlatformWindowFrame(this AppBuilder builder)
        {
            if (OperatingSystem.IsWindows() && !OperatingSystem.IsWindowsVersionAtLeast(10, 0, 22000))
            {
                Window.WindowStateProperty.Changed.AddClassHandler<Window>((window, _) => FixDwmFrameOnWindows(window));
                Control.LoadedEvent.AddClassHandler<Window>((window, _) => FixDwmFrameOnWindows(window));
            }
            else if (OperatingSystem.IsMacOS())
            {
                Control.LoadedEvent.AddClassHandler<Window>((window, _) => ConfigureMacOSNativeTitleBar(window));
            }

            return builder;
        }

        public static void SetupWindow(Window window)
        {
            if (OperatingSystem.IsWindows())
            {
                window.WindowDecorations = WindowDecorations.BorderOnly;
                window.SystemDecorations = WindowDecorations.BorderOnly;
                window.ExtendClientAreaToDecorationsHint = true;
                window.BorderThickness = new Thickness(1);
                UpdateWindowStatePadding(window);
            }
            else if (OperatingSystem.IsMacOS())
            {
                window.WindowDecorations = WindowDecorations.Full;
                window.SystemDecorations = WindowDecorations.Full;
                window.ExtendClientAreaToDecorationsHint = true;
                window.BorderThickness = new Thickness(1);
                UpdateWindowStatePadding(window);
                window.Opened += (_, _) => ConfigureMacOSNativeTitleBar(window);
            }
        }

        public static void UpdateWindowStatePadding(Window window)
        {
            if (!OperatingSystem.IsWindows())
                return;

            if (window.WindowState == WindowState.Maximized)
            {
                window.BorderThickness = new Thickness(0);
                window.Padding = new Thickness(8, 6, 8, 8);
            }
            else
            {
                window.BorderThickness = new Thickness(1);
                window.Padding = new Thickness(0);
            }
        }

        private static void FixDwmFrameOnWindows(Window window)
        {
            if (!OperatingSystem.IsWindows())
                return;

            Dispatcher.UIThread.Post(() =>
            {
                var handle = window.TryGetPlatformHandle();
                if (handle == null)
                    return;

                var margins = new Margins
                {
                    Left = 1,
                    Right = 1,
                    Top = 1,
                    Bottom = 1
                };

                DwmExtendFrameIntoClientArea(handle.Handle, ref margins);
            }, DispatcherPriority.Render);
        }

        private static void ConfigureMacOSNativeTitleBar(Window window)
        {
            if (!OperatingSystem.IsMacOS())
                return;

            Dispatcher.UIThread.Post(() =>
            {
                var handle = window.TryGetPlatformHandle();
                if (handle == null)
                    return;

                ObjC.objc_msgSend_SetIntPtr(handle.Handle, ObjC.Selectors.SetToolbarStyle, NSWindowToolbarStyleUnified);
                SetupMacOSWindowNotificationObserver(window, handle.Handle);
                AdjustMacOSWindowButtons(handle.Handle);
            }, DispatcherPriority.Render);
        }

        private static void SetupMacOSWindowNotificationObserver(Window window, IntPtr nativeWindow)
        {
            if (s_macOSNotificationObservers.ContainsKey(window))
                return;

            var observerClass = ObjC.GetWindowFrameObserverClass();
            if (observerClass == IntPtr.Zero)
                return;

            var observer = ObjC.objc_msgSend_IntPtr(ObjC.objc_msgSend_IntPtr(observerClass, ObjC.Selectors.Alloc), ObjC.Selectors.Init);
            if (observer == IntPtr.Zero)
                return;

            var notificationCenter = ObjC.objc_msgSend_IntPtr(ObjC.Classes.NSNotificationCenter, ObjC.Selectors.DefaultCenter);
            foreach (var notificationName in s_macOSWindowFrameNotifications)
            {
                var nativeNotificationName = ObjC.CreateNSString(notificationName);
                ObjC.objc_msgSend_AddObserver(notificationCenter, ObjC.Selectors.AddObserver, observer, ObjC.Selectors.WindowFrameChanged, nativeNotificationName, nativeWindow);
            }

            s_macOSNotificationObservers[window] = observer;
            window.Closed += (_, _) => RemoveMacOSWindowNotificationObserver(window);
        }

        private static void RemoveMacOSWindowNotificationObserver(Window window)
        {
            if (!s_macOSNotificationObservers.Remove(window, out var observer))
                return;

            var notificationCenter = ObjC.objc_msgSend_IntPtr(ObjC.Classes.NSNotificationCenter, ObjC.Selectors.DefaultCenter);
            ObjC.objc_msgSend_IntPtr(notificationCenter, ObjC.Selectors.RemoveObserver, observer);
            ObjC.objc_msgSend_IntPtr(observer, ObjC.Selectors.Release);
        }

        private static void OnMacOSWindowFrameNotification(IntPtr notification)
        {
            if (!OperatingSystem.IsMacOS() || notification == IntPtr.Zero)
                return;

            var nativeWindow = ObjC.objc_msgSend_IntPtr(notification, ObjC.Selectors.Object);
            if (nativeWindow != IntPtr.Zero)
                AdjustMacOSWindowButtons(nativeWindow);
        }

        private static void AdjustMacOSWindowButtons(IntPtr nativeWindow)
        {
            var closeButton = ObjC.objc_msgSend_IntPtr(nativeWindow, ObjC.Selectors.StandardWindowButton, 0);
            if (closeButton == IntPtr.Zero)
                return;

            var closeFrame = ObjC.GetFrame(closeButton);

            for (nint button = 0; button <= 2; button++)
            {
                var nativeButton = ObjC.objc_msgSend_IntPtr(nativeWindow, ObjC.Selectors.StandardWindowButton, button);
                if (nativeButton == IntPtr.Zero)
                    continue;

                var frame = ObjC.GetFrame(nativeButton);
                var x = MacOSWindowButtonOriginX + frame.X - closeFrame.X;
                ObjC.objc_msgSend_SetFrameOrigin(nativeButton, ObjC.Selectors.SetFrameOrigin, new NSPoint(x, MacOSWindowButtonOriginY));
            }
        }

        [DllImport("dwmapi.dll")]
        private static extern int DwmExtendFrameIntoClientArea(IntPtr hwnd, ref Margins margins);

        [StructLayout(LayoutKind.Sequential)]
        private struct Margins
        {
            public int Left;
            public int Right;
            public int Top;
            public int Bottom;
        }

        private static class ObjC
        {
            private const string ObjectiveCLibrary = "/usr/lib/libobjc.A.dylib";
            private static readonly WindowFrameNotificationCallback s_windowFrameNotificationCallback = HandleWindowFrameNotification;
            private static IntPtr s_windowFrameObserverClass;

            [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
            private delegate void WindowFrameNotificationCallback(IntPtr self, IntPtr selector, IntPtr notification);

            public static class Selectors
            {
                public static readonly IntPtr AddObserver = sel_registerName("addObserver:selector:name:object:");
                public static readonly IntPtr Alloc = sel_registerName("alloc");
                public static readonly IntPtr DefaultCenter = sel_registerName("defaultCenter");
                public static readonly IntPtr Frame = sel_registerName("frame");
                public static readonly IntPtr Init = sel_registerName("init");
                public static readonly IntPtr Object = sel_registerName("object");
                public static readonly IntPtr Release = sel_registerName("release");
                public static readonly IntPtr RemoveObserver = sel_registerName("removeObserver:");
                public static readonly IntPtr SetFrameOrigin = sel_registerName("setFrameOrigin:");
                public static readonly IntPtr SetToolbarStyle = sel_registerName("setToolbarStyle:");
                public static readonly IntPtr StandardWindowButton = sel_registerName("standardWindowButton:");
                public static readonly IntPtr StringWithUtf8String = sel_registerName("stringWithUTF8String:");
                public static readonly IntPtr WindowFrameChanged = sel_registerName("easyconWindowFrameChanged:");
            }

            public static class Classes
            {
                public static readonly IntPtr NSNotificationCenter = objc_getClass("NSNotificationCenter");
                public static readonly IntPtr NSObject = objc_getClass("NSObject");
                public static readonly IntPtr NSString = objc_getClass("NSString");
            }

            public static IntPtr CreateNSString(string value)
            {
                return objc_msgSend_IntPtr_String(Classes.NSString, Selectors.StringWithUtf8String, value);
            }

            public static IntPtr GetWindowFrameObserverClass()
            {
                if (s_windowFrameObserverClass != IntPtr.Zero)
                    return s_windowFrameObserverClass;

                var existingClass = objc_lookUpClass(MacOSWindowFrameObserverClassName);
                if (existingClass != IntPtr.Zero)
                {
                    s_windowFrameObserverClass = existingClass;
                    return s_windowFrameObserverClass;
                }

                var observerClass = objc_allocateClassPair(Classes.NSObject, MacOSWindowFrameObserverClassName, 0);
                if (observerClass == IntPtr.Zero)
                    return IntPtr.Zero;

                var callbackPointer = Marshal.GetFunctionPointerForDelegate(s_windowFrameNotificationCallback);
                class_addMethod(observerClass, Selectors.WindowFrameChanged, callbackPointer, "v@:@");
                objc_registerClassPair(observerClass);
                s_windowFrameObserverClass = observerClass;
                return s_windowFrameObserverClass;
            }

            public static NSRect GetFrame(IntPtr receiver)
            {
                if (RuntimeInformation.ProcessArchitecture == Architecture.X64)
                {
                    objc_msgSend_NSRect_stret(out var frame, receiver, Selectors.Frame);
                    return frame;
                }

                return objc_msgSend_NSRect(receiver, Selectors.Frame);
            }

            private static void HandleWindowFrameNotification(IntPtr self, IntPtr selector, IntPtr notification)
            {
                OnMacOSWindowFrameNotification(notification);
            }

            [DllImport(ObjectiveCLibrary, EntryPoint = "sel_registerName")]
            private static extern IntPtr sel_registerName(string selectorName);

            [DllImport(ObjectiveCLibrary, EntryPoint = "objc_getClass")]
            private static extern IntPtr objc_getClass(string className);

            [DllImport(ObjectiveCLibrary, EntryPoint = "objc_lookUpClass")]
            private static extern IntPtr objc_lookUpClass(string className);

            [DllImport(ObjectiveCLibrary, EntryPoint = "objc_allocateClassPair")]
            private static extern IntPtr objc_allocateClassPair(IntPtr superclass, string name, nuint extraBytes);

            [DllImport(ObjectiveCLibrary, EntryPoint = "objc_registerClassPair")]
            private static extern void objc_registerClassPair(IntPtr cls);

            [DllImport(ObjectiveCLibrary, EntryPoint = "class_addMethod")]
            private static extern bool class_addMethod(IntPtr cls, IntPtr name, IntPtr imp, string types);

            [DllImport(ObjectiveCLibrary, EntryPoint = "objc_msgSend")]
            public static extern IntPtr objc_msgSend_IntPtr(IntPtr receiver, IntPtr selector);

            [DllImport(ObjectiveCLibrary, EntryPoint = "objc_msgSend")]
            public static extern IntPtr objc_msgSend_IntPtr(IntPtr receiver, IntPtr selector, nint argument);

            [DllImport(ObjectiveCLibrary, EntryPoint = "objc_msgSend")]
            public static extern IntPtr objc_msgSend_IntPtr_String(IntPtr receiver, IntPtr selector, [MarshalAs(UnmanagedType.LPUTF8Str)] string argument);

            [DllImport(ObjectiveCLibrary, EntryPoint = "objc_msgSend")]
            public static extern void objc_msgSend_SetIntPtr(IntPtr receiver, IntPtr selector, nint argument);

            [DllImport(ObjectiveCLibrary, EntryPoint = "objc_msgSend")]
            public static extern void objc_msgSend_SetFrameOrigin(IntPtr receiver, IntPtr selector, NSPoint origin);

            [DllImport(ObjectiveCLibrary, EntryPoint = "objc_msgSend")]
            private static extern NSRect objc_msgSend_NSRect(IntPtr receiver, IntPtr selector);

            [DllImport(ObjectiveCLibrary, EntryPoint = "objc_msgSend_stret")]
            private static extern void objc_msgSend_NSRect_stret(out NSRect result, IntPtr receiver, IntPtr selector);

            [DllImport(ObjectiveCLibrary, EntryPoint = "objc_msgSend")]
            public static extern void objc_msgSend_AddObserver(IntPtr receiver, IntPtr selector, IntPtr observer, IntPtr notificationSelector, IntPtr notificationName, IntPtr notificationSender);
        }

        [StructLayout(LayoutKind.Sequential)]
        private struct NSPoint
        {
            public double X;
            public double Y;

            public NSPoint(double x, double y)
            {
                X = x;
                Y = y;
            }
        }

        [StructLayout(LayoutKind.Sequential)]
        private struct NSSize
        {
            public double Width;
            public double Height;
        }

        [StructLayout(LayoutKind.Sequential)]
        private struct NSRect
        {
            public NSPoint Origin;
            public NSSize Size;

            public double X => Origin.X;
        }
    }
