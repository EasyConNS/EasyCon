using EasyCon.Core.Capabilities;
using EasyCon.Core.Flow;
using OpenCvSharp;
using System.Text.Json;

namespace EasyCon.Tests.Flow;

/// <summary>
/// 节点目录（前端渲染与后端执行的单一事实源）与单节点试跑：
/// 目录里的每个节点必须真能被运行时执行（防「目录登记了、运行时没实现」的漂移）。
/// </summary>
[TestFixture]
public class FlowNodeCatalogTests
{
    [Test]
    public void Catalog_TypeIdsAreUniqueAndLayered()
    {
        var types = FlowNodeCatalog.Nodes.Select(n => n.Type).ToList();

        Assert.Multiple(() =>
        {
            Assert.That(types, Is.Unique);
            Assert.That(types, Does.Contain("capture.frame"));
            Assert.That(types, Does.Contain("ocr.text"));
            Assert.That(FlowNodeCatalog.Nodes.All(n => n.Layer is "flow" or "sense" or "decision" or "actuation"),
                Is.True, "layer 必须是四类之一");
            Assert.That(FlowNodeCatalog.Nodes.All(n => n.Summary.Length > 0), Is.True);
        });
    }

    [Test]
    public void Catalog_ActuationLayerIsNotTrialRunnable()
    {
        Assert.Multiple(() =>
        {
            Assert.That(FlowNodeCatalog.IsTrialAllowed("capture.frame"), Is.True);
            Assert.That(FlowNodeCatalog.IsTrialAllowed("ocr.text"), Is.True);
            Assert.That(FlowNodeCatalog.IsTrialAllowed("compare"), Is.True);
            Assert.That(FlowNodeCatalog.IsTrialAllowed("pad.key"), Is.False, "试跑不得驱动设备");
            Assert.That(FlowNodeCatalog.IsTrialAllowed("script.run"), Is.False, "脚本可驱动设备，试跑拒绝");
        });
    }

    /// <summary>
    /// 目录 ↔ 运行时一致性：逐个执行目录里的节点，只允许出现「缺能力/缺参数」这类错误，
    /// 不允许出现「未知节点类型」——那说明目录登记了运行时没实现的节点。
    /// </summary>
    [Test]
    public void Catalog_EveryNodeTypeIsImplementedByRuntime()
    {
        var source = new StubCapture();
        var graph = new FlowGraph { Name = "catalog-probe", Nodes = [] };
        var runtime = new FlowNodeRuntime(graph, new FlowHostContext { Capture = source });

        var unimplemented = new List<string>();
        foreach (FlowNodeSpec spec in FlowNodeCatalog.Nodes)
        {
            var node = new FlowNode { Id = $"probe-{spec.Type}", Type = spec.Type };
            graph.Nodes.Add(node);
            try
            {
                runtime.Execute(node, CancellationToken.None);
            }
            catch (Exception ex)
            {
                // 能力缺失/参数缺失都是预期内的（探针没有设备）；「未知节点类型」才是漂移
                if (ex.Message.Contains("未知节点类型", StringComparison.Ordinal))
                    unimplemented.Add(spec.Type);
            }
        }

        Assert.That(unimplemented, Is.Empty, "目录里有节点没有运行时实现");
    }

    [Test]
    public void CatalogJson_CarriesNodesSlowFieldsAndKeyNames()
    {
        using var doc = JsonDocument.Parse(FlowNodeCatalog.Json());
        var root = doc.RootElement;
        var nodes = root.GetProperty("nodes");

        Assert.Multiple(() =>
        {
            Assert.That(nodes.GetArrayLength(), Is.EqualTo(FlowNodeCatalog.Nodes.Count));
            var capture = nodes.EnumerateArray().First(n => n.GetProperty("type").GetString() == "capture.frame");
            Assert.That(capture.GetProperty("layer").GetString(), Is.EqualTo("sense"));
            var ports = capture.GetProperty("ports").EnumerateArray().ToList();
            Assert.That(ports.Any(p => p.GetProperty("name").GetString() == "image"
                && p.GetProperty("kind").GetString() == "data-out"), Is.True);
            Assert.That(root.GetProperty("slow").GetArrayLength(), Is.EqualTo(3));
            Assert.That(root.GetProperty("keys").EnumerateArray().Select(k => k.GetString()), Does.Contain("A"));
            Assert.That(root.GetProperty("ocrBackends").EnumerateArray().Select(k => k.GetString()),
                Is.EquivalentTo(new[] { "none", "tesseract", "ppocr" }));
        });
    }

    private sealed class StubCapture : ICaptureSource
    {
        public long? FrameIndex => 1;

        public string? CaptureFrame(int x, int y, int width, int height)
        {
            using var mat = new Mat(8, 8, MatType.CV_8UC3, Scalar.Blue);
            return Convert.ToBase64String(mat.ToBytes(".png"));
        }
    }
}