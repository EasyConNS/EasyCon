using EasyCon.Core.Capabilities;
using EasyCon.Core.Hosting;
using EasyCon.Core.Script;
using EasyCon.Tests.Support;

namespace EasyCon.Tests.Capabilities;

[TestFixture]
public class NetImageTensorTests
{
    // Row-major RGB pixels: red, green, blue, (10, 20, 30).
    private const string Image = "iVBORw0KGgoAAAANSUhEUgAAAAIAAAACCAIAAAD91JpzAAAAFklEQVQIHWP8z8DA+J+BkYHhP5eIPAAaeAM+1trlfQAAAABJRU5ErkJggg==";
    private static readonly float[] RgbChw = [255, 0, 0, 10, 0, 255, 0, 20, 0, 0, 255, 30];

    [TestCase("NET_IMAGE($image, 2, 2, \"rgb\")", 1f, 0f, 1,
        TestName = "NetImage_Rgb_PreservesDistinctChannelsInChwOrder")]
    [TestCase("NET_SCALE(NET_IMAGE($image, 2, 2, \"rgb\"), 1.0 / 255, 0)", 1f / 255, 0f, 2,
        TestName = "NetScale_PreservesRgbChannels")]
    [TestCase("PPOCR_TENSOR($image, 2, 2)", 1f / 255, -0.25f, 2,
        TestName = "PpocrTensor_DefaultRec_PreservesRgbChannels")]
    [TestCase("PPOCR_TENSOR($image, 2, 2, \"rec\")", 1f / 255, -0.25f, 2,
        TestName = "PpocrTensor_ExplicitRec_PreservesRgbChannels")]
    [TestCase("PPOCR_TENSOR($image, 2, 2, \"det\")", 1f / 255 / 0.5f, -0.5f, 2,
        TestName = "PpocrTensor_Det_PreservesRgbChannels")]
    public void RgbTensor_ContainsExpectedPixels(string expression, float scale, float offset, int tensorCount)
    {
        RecordedTensor[] tensors = Execute(expression);

        Assert.That(tensors, Has.Length.EqualTo(tensorCount));
        Assert.Multiple(() =>
        {
            Assert.That(tensors[0].Shape, Is.EqualTo(new[] { 1, 3, 2, 2 }));
            Assert.That(tensors[0].Data, Is.EqualTo(RgbChw), "Raw RGB must contain R, G and B planes, not three copies of R.");
            Assert.That(tensors[^1].Shape, Is.EqualTo(new[] { 1, 3, 2, 2 }));
            Assert.That(tensors[^1].Data, Is.EqualTo(RgbChw.Select(value => value * scale + offset)).Within(1e-6f));
        });
    }

    [TestCase("NET_IMAGE($image, 2, 2, \"gray\")")]
    [TestCase("NET_IMAGE($image, 2, 2)")]
    public void GrayTensor_ContainsExpectedLuminance(string expression)
    {
        RecordedTensor[] tensors = Execute(expression);

        Assert.That(tensors, Has.Length.EqualTo(1));
        Assert.Multiple(() =>
        {
            Assert.That(tensors[0].Shape, Is.EqualTo(new[] { 1, 1, 2, 2 }));
            Assert.That(tensors[0].Data, Is.EqualTo(new float[] { 76, 150, 29, 18 }));
        });
    }

    private static RecordedTensor[] Execute(string expression)
    {
        string source = $$"""
            $image = "{{Image}}"
            $tensor = {{expression}}
            PRINT $tensor
            """;
        EasyScriptEngine engine = new();
        IScriptSession session = engine.FromSource(source,
            new ScriptHostOptions { Compile = ScriptCompileProfiles.Interactive([]) });
        Assert.That(session.Info.Diagnostics.Where(diagnostic => diagnostic.IsError), Is.Empty, "Script must compile.");
        Assert.That(session.Info.Image, Is.Not.Null);

        RecordingInference inference = new();
        RecordingIo io = new();
        using CapabilityLease lease = ScriptHostAssembler.Assemble(new ScriptHostContext
        {
            Console = new ConsoleIoAdapter(io),
            Inference = inference,
            EnableOcr = false,
        });
        session.Run(CancellationToken.None, lease.Capabilities);

        Assert.That(io.Lines, Is.EqualTo(new[] { inference.Tensors.Count.ToString() }), "Script must return the held tensor handle.");
        return inference.Tensors.ToArray();
    }

    private sealed record RecordedTensor(float[] Data, int[] Shape);

    private sealed class RecordingInference : IInference
    {
        public List<RecordedTensor> Tensors { get; } = [];
        public int[]? LastOutputShape => null;

        public int HoldTensor(float[] data, int[] shape)
        {
            Tensors.Add(new RecordedTensor((float[])data.Clone(), (int[])shape.Clone()));
            return Tensors.Count;
        }

        public int TransformTensor(int handle, float scale, float offset)
        {
            RecordedTensor tensor = Tensors[handle - 1];
            return HoldTensor(tensor.Data.Select(value => value * scale + offset).ToArray(), tensor.Shape);
        }

        public int Load(string modelPath) => throw new NotSupportedException();
        public float[]? RunHeld(int session, int tensorHandle) => throw new NotSupportedException();
        public void FreeTensor(int handle) { }
        public void Unload(int session) { }
        public void Dispose() { }
    }
}