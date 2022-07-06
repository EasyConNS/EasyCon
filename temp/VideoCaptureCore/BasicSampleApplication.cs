//  ---------------------------------------------------------------------------------
//  Copyright (c) Microsoft Corporation.  All rights reserved.
// 
//  The MIT License (MIT)
// 
//  Permission is hereby granted, free of charge, to any person obtaining a copy
//  of this software and associated documentation files (the "Software"), to deal
//  in the Software without restriction, including without limitation the rights
//  to use, copy, modify, merge, publish, distribute, sublicense, and/or sell
//  copies of the Software, and to permit persons to whom the Software is
//  furnished to do so, subject to the following conditions:
// 
//  The above copyright notice and this permission notice shall be included in
//  all copies or substantial portions of the Software.
// 
//  THE SOFTWARE IS PROVIDED "AS IS", WITHOUT WARRANTY OF ANY KIND, EXPRESS OR
//  IMPLIED, INCLUDING BUT NOT LIMITED TO THE WARRANTIES OF MERCHANTABILITY,
//  FITNESS FOR A PARTICULAR PURPOSE AND NONINFRINGEMENT. IN NO EVENT SHALL THE
//  AUTHORS OR COPYRIGHT HOLDERS BE LIABLE FOR ANY CLAIM, DAMAGES OR OTHER
//  LIABILITY, WHETHER IN AN ACTION OF CONTRACT, TORT OR OTHERWISE, ARISING FROM,
//  OUT OF OR IN CONNECTION WITH THE SOFTWARE OR THE USE OR OTHER DEALINGS IN
//  THE SOFTWARE.
//  ---------------------------------------------------------------------------------

using System;
using System.Numerics;
using Windows.Graphics.Capture;
using Windows.UI.Composition;
using Windows.Graphics.DirectX.Direct3D11;
using VideoCaptureCore.helper;
using Windows.Graphics.Imaging;
using System.IO;
using System.Threading.Tasks;

namespace VideoCaptureCore
{
    public class BasicSampleApplication : IDisposable
    {
        private Compositor compositor;
        private ContainerVisual containerVisual;

        private SpriteVisual spriteVisual;
        private CompositionSurfaceBrush brush;

        private IDirect3DDevice device;
        private BasicCapture capture;

        public BasicSampleApplication(Compositor c)
        {
            compositor = c;
            device = Direct3D11Helper.CreateDevice();

            // Setup the root.
            containerVisual = compositor.CreateContainerVisual();
            containerVisual.RelativeSizeAdjustment = Vector2.One;

            // Setup the content.
            brush = compositor.CreateSurfaceBrush();
            brush.HorizontalAlignmentRatio = 0.1f;
            brush.VerticalAlignmentRatio = 0.1f;
            brush.Stretch = CompositionStretch.Uniform;
            spriteVisual = compositor.CreateSpriteVisual();
            spriteVisual.AnchorPoint = new Vector2(0.1f);
            spriteVisual.RelativeOffsetAdjustment = new Vector3(0.1f, 0.1f, 0);
            spriteVisual.RelativeSizeAdjustment = Vector2.One;
            //spriteVisual.Size = new Vector2(-80, -80);
            spriteVisual.Brush = brush;
            containerVisual.Children.InsertAtTop(spriteVisual);
        }

        public Visual Visual => containerVisual;

        public void Dispose()
        {
            StopCapture();
            compositor = null;
            containerVisual.Dispose();
            spriteVisual.Dispose();
            brush.Dispose();
            device.Dispose();
        }

        public void StartCaptureFromItem(IntPtr hwnd)
        {
            GraphicsCaptureItem item = CaptureHelper.CreateItemForWindow(hwnd);
            if (item != null)
            {
                StopCapture();
                capture = new BasicCapture(device, item);

                var surface = capture.CreateSurface(compositor);
                brush.Surface = surface;

                capture.StartCapture();
            }
        }

        public async Task<Stream> GetBitmapAsync()
        {
            var softwareBitmap = await capture.GetCaptureAsync();
            var stream = new Windows.Storage.Streams.InMemoryRandomAccessStream();
            var encoder = await BitmapEncoder.CreateAsync(BitmapEncoder.PngEncoderId, stream);
            encoder.SetSoftwareBitmap(softwareBitmap);
            await encoder.FlushAsync();
            return stream.AsStream();
        }

        public void StopCapture()
        {
            capture?.Dispose();
            brush.Surface = null;
        }
    }
}
