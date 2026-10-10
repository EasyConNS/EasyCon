using EasyCon.Core.Capabilities;
using EasyCon.Core.Flow;
using EasyCon.Script;
using EasyScript;
using OpenCvSharp;
using System.Text.Json;

namespace EasyCon.Tests.Flow;

/// <summary>
/// Flow 执行引擎：exec 推进、数据引用、条件分支、脚本节点、看门狗。
/// 全部无硬件（stub capture/pad），真模型链路由 PokemonStats_RecAndCompute 覆盖。
/// </summary>
[TestFixture]
public class FlowExecutorTests
{
    private sealed class StubPad : IPadInput
    {
        public List<string> Clicks = new();

        public void ClickButtons(GamePadKey key, int duration, CancellationToken token)
            => Clicks.Add($"{key}:{duration}");

        public void PressButtons(GamePadKey key) { }

        public void ReleaseButtons(GamePadKey key) { }

        public void ClickStick(GamePadKey key, byte x, byte y, int duration, CancellationToken token) { }

        public void SetStick(GamePadKey key, byte x, byte y) { }

        public void ChangeAmiibo(uint index) { }
    }

    private sealed class StubOcr(string text) : IOcrService
    {
        public bool Disposed { get; private set; }

        public string Backend => "stub";
        public int LastConfidence { get; private set; }
        public bool Init(OcrConfig cfg) => true;
        public string Recognize(ImageRef image, OcrQuery query)
        {
            LastConfidence = 88;
            return text;
        }
        public void Dispose() => Disposed = true;
    }

    private static FlowHostContext Context(
        StubPad? pad = null, string? frameText = null, string appDir = "", IOcrService? ocr = null)
    {
        ICaptureSource? capture = null;
        if (frameText != null)
        {
            using var m = new Mat(32, 64, MatType.CV_8UC3, Scalar.Blue);
            var b64 = Convert.ToBase64String(m.ToBytes(".png"));
            capture = new DelegateCaptureSource((_, _, _, _) => b64);
        }
        return new FlowHostContext
        {
            Capture = capture,
            Ocr = ocr ?? (frameText != null ? new StubOcr(frameText) : null),
            Pad = pad,
            AppDir = appDir,
        };
    }

    private static FlowGraph Graph(string json) => FlowGraph.Parse(json);

    [Test]
    public void LinearFlow_ExecutesInOrder()
    {
        var graph = Graph("""
        {
          "nodes": [
            { "id": "a", "type": "wait", "params": { "ms": 1 } },
            { "id": "b", "type": "end" }
          ],
          "exec": [ ["a", "b"] ]
        }
        """);
        var report = new FlowExecutor(graph, Context()).Run(new CancellationTokenSource().Token);
        Assert.That(report.Completed, Is.True);
        Assert.That(report.Error, Is.Null);
        Assert.That(report.Steps, Is.EqualTo(2));   // a、b 各一步
    }

    [Test]
    public void Compare_BranchesByLiteralInputs()
    {
        var graph = Graph("""
        {
          "nodes": [
            { "id": "chk", "type": "compare", "params": { "op": ">=" }, "inputs": { "a": 5, "b": 3 } },
            { "id": "yes", "type": "wait", "params": { "ms": 1 } },
            { "id": "no",  "type": "wait", "params": { "ms": 1 } }
          ],
          "exec": [
            ["chk.true", "yes"],
            ["chk.false", "no"]
          ]
        }
        """);
        var report = new FlowExecutor(graph, Context()).Run(new CancellationTokenSource().Token);
        Assert.That(report.Completed, Is.True);
        Assert.That(report.Records["yes"].ExecCount, Is.EqualTo(1), "5 >= 3 → true 出口");
        Assert.That(report.Records.ContainsKey("no"), Is.False);
    }

    [Test]
    public void OcrNode_FlowsTextIntoCondition()
    {
        // capture.frame → ocr.text → compare("ATTACK 31" 包含…走数值比较 31 >= 30)
        // v1 无 text.contains 节点，用 OCR 输出 conf 作为比较量验证数据边
        var graph = Graph("""
        {
          "nodes": [
            { "id": "cap", "type": "capture.frame" },
            { "id": "ocr", "type": "ocr.text", "params": { "lang": "chi_sim" },
              "inputs": { "image": { "node": "cap", "port": "image" } } },
            { "id": "chk", "type": "compare", "params": { "op": ">=" },
              "inputs": { "a": { "node": "ocr", "port": "conf" }, "b": 50 } },
            { "id": "hit", "type": "wait", "params": { "ms": 1 } }
          ],
          "exec": [
            ["start", "cap"], ["cap", "ocr"], ["ocr", "chk"], ["chk.true", "hit"]
          ]
        }
        """);
        var report = new FlowExecutor(graph, Context(frameText: "ATTACK 31")).Run(new CancellationTokenSource().Token);
        Assert.That(report.Completed, Is.True);
        Assert.That(report.Records["ocr"].LastOutputs["text"], Is.EqualTo("ATTACK 31"));
        Assert.That(report.Records["hit"].ExecCount, Is.EqualTo(1), "conf 88 >= 50 → true 出口");
    }

    [Test]
    public void ScriptNode_PassesArgAndCapturesPrint()
    {
        var appDir = Path.Combine(Path.GetTempPath(), $"easycon-flow-{Guid.NewGuid():N}");
        Directory.CreateDirectory(appDir);
        File.WriteAllText(Path.Combine(appDir, "hello.ecs"),
            "$a = ARG(1)\nPRINT \"arg=\" & $a\nPRINT \"end\"\n");

        var graph = Graph("""
        {
          "nodes": [
            { "id": "run", "type": "script.run", "params": { "file": "hello.ecs" },
              "inputs": { "arg1": "world" } },
            { "id": "chk", "type": "compare", "params": { "op": "==" },
              "inputs": { "a": { "node": "run", "port": "ok" }, "b": 1 } },
            { "id": "ok", "type": "wait", "params": { "ms": 1 } }
          ],
          "exec": [ ["start", "run"], ["run", "chk"], ["chk.true", "ok"] ]
        }
        """);
        var report = new FlowExecutor(graph, Context(pad: new StubPad(), appDir: appDir)).Run(new CancellationTokenSource().Token);
        Assert.That(report.Error, Is.Null, report.Error ?? "");
        var run = report.Records["run"];
        Assert.That(run.LastOutputs["ok"], Is.EqualTo(1));
        Assert.That(run.LastOutputs["logs"], Does.Contain("arg=world"));
        Assert.That(report.Records["ok"].ExecCount, Is.EqualTo(1));
    }

    [Test]
    public void ScriptNode_InlineScriptRunsAndTakesPrecedenceOverFile()
    {
        // 内联 script（多行文本）非空时优先于 file：即写即跑，无脚本目录上下文
        var appDir = Path.Combine(Path.GetTempPath(), $"easycon-flow-{Guid.NewGuid():N}");
        Directory.CreateDirectory(appDir);
        File.WriteAllText(Path.Combine(appDir, "should-not-run.ecs"), "PRINT \"from-file\"");

        var graph = Graph("""
        {
          "nodes": [
            { "id": "run", "type": "script.run",
              "params": { "script": "$n = ARG(1)\nPRINT \"inline=\" & $n", "file": "should-not-run.ecs" },
              "inputs": { "arg1": "7" } },
            { "id": "end", "type": "end" }
          ],
          "exec": [ ["start", "run"], ["run", "end"] ]
        }
        """);
        var report = new FlowExecutor(graph, Context(appDir: appDir)).Run(new CancellationTokenSource().Token);
        Assert.That(report.Error, Is.Null, report.Error ?? "");
        var run = report.Records["run"];
        Assert.That(run.LastOutputs["ok"], Is.EqualTo(1));
        Assert.That(run.LastOutputs["logs"], Is.EqualTo("inline=7"), "应执行内联脚本而不是 file 指向的文件");
    }

    [Test]
    public void ScriptNode_RequiresScriptOrFile()
    {
        var graph = Graph("""
        {
          "nodes": [
            { "id": "run", "type": "script.run", "params": {} },
            { "id": "end", "type": "end" }
          ],
          "exec": [ ["run", "end"] ]
        }
        """);
        var report = new FlowExecutor(graph, Context()).Run(new CancellationTokenSource().Token);
        Assert.That(report.Error, Does.Contain("script").And.Contain("file"), "缺参错误应同时提示两种脚本来源");
    }

    [Test]
    public void PadSequence_DrivesStubPad()
    {
        var pad = new StubPad();
        var graph = Graph("""
        {
          "nodes": [
            { "id": "seq", "type": "pad.sequence", "params": { "seq": "A,100; ↓,200" } },
            { "id": "end", "type": "end" }
          ],
          "exec": [ ["seq", "end"] ]
        }
        """);
        var report = new FlowExecutor(graph, Context(pad: pad)).Run(new CancellationTokenSource().Token);
        Assert.That(report.Error, Is.Null);
        Assert.That(pad.Clicks, Is.EqualTo(new[] { "A:100", "DOWN:200" }), "↓ 归一化为 DOWN");
    }

    [Test]
    public void Watchdog_StopsUnboundedLoop()
    {
        var graph = Graph("""
        {
          "maxSteps": 5,
          "nodes": [
            { "id": "a", "type": "wait", "params": { "ms": 1 } }
          ],
          "exec": [ ["a", "a"] ]
        }
        """);
        var report = new FlowExecutor(graph, Context()).Run(new CancellationTokenSource().Token);
        Assert.That(report.WatchdogTriggered, Is.True);
        Assert.That(report.Steps, Is.LessThanOrEqualTo(6));
    }

    [Test]
    public void NodeError_StopsWithErrorAndNodeId()
    {
        var graph = Graph("""
        {
          "nodes": [
            { "id": "ocr", "type": "ocr.text", "params": { "lang": "chi_sim" } }
          ],
          "exec": []
        }
        """);
        var report = new FlowExecutor(graph, Context()).Run(new CancellationTokenSource().Token);
        Assert.That(report.Error, Does.Contain("OCR"));
        Assert.That(report.ErrorNodeId, Is.EqualTo("ocr"));
    }

    [Test]
    public void DataOnlyUpstream_IsEvaluatedLazily()
    {
        // cap 不在 exec 路径上，只作为 ocr.image 的数据来源：惰性求值必须回填输出，
        // 否则纯数据边永远取不到值（旧实现把结果写进了报告记录而非输出表）。
        var graph = Graph("""
        {
          "nodes": [
            { "id": "start", "type": "start" },
            { "id": "cap", "type": "capture.frame" },
            { "id": "ocr", "type": "ocr.text",
              "inputs": { "image": { "node": "cap", "port": "image" } } },
            { "id": "chk", "type": "compare", "params": { "op": ">=" },
              "inputs": { "a": { "node": "ocr", "port": "conf" }, "b": 50 } },
            { "id": "hit", "type": "wait", "params": { "ms": 1 } }
          ],
          "exec": [ ["start", "ocr"], ["ocr", "chk"], ["chk.true", "hit"] ]
        }
        """);
        var report = new FlowExecutor(graph, Context(frameText: "DITTO 25")).Run(new CancellationTokenSource().Token);
        Assert.That(report.Error, Is.Null, report.Error ?? "");
        Assert.That(report.Records["ocr"].LastOutputs["text"], Is.EqualTo("DITTO 25"));
        Assert.That(report.Records["hit"].ExecCount, Is.EqualTo(1), "惰性上游的 conf 88 >= 50 → true 出口");
    }

    [Test]
    public void ConditionalBranch_LabeledPortBeatsPlainEdge()
    {
        var graph = Graph("""
        {
          "nodes": [
            { "id": "chk", "type": "compare", "params": { "op": ">" }, "inputs": { "a": 5, "b": 3 } },
            { "id": "yes", "type": "wait", "params": { "ms": 1 } },
            { "id": "plain", "type": "wait", "params": { "ms": 1 } }
          ],
          "exec": [ ["chk", "plain"], ["chk.true", "yes"] ]
        }
        """);
        var report = new FlowExecutor(graph, Context()).Run(new CancellationTokenSource().Token);
        Assert.That(report.Records["yes"].ExecCount, Is.EqualTo(1), "条件出口精确匹配优先于无标签边");
        Assert.That(report.Records.ContainsKey("plain"), Is.False);
    }

    [Test]
    public void DataEdgeCycle_IsRejectedInsteadOfRecursing()
    {
        var graph = Graph("""
        {
          "nodes": [
            { "id": "a", "type": "compare", "params": { "op": "==" },
              "inputs": { "a": { "node": "a", "port": "x" }, "b": 1 } }
          ],
          "exec": []
        }
        """);
        var report = new FlowExecutor(graph, Context()).Run(new CancellationTokenSource().Token);
        Assert.That(report.Error, Does.Contain("环"));
    }

    [Test]
    public void ScriptNode_BorrowsSharedCapabilities()
    {
        // script.run 内层装配必须借用外层运行的 OCR：否则内层租约释放后，
        // 同一张图后续的 ocr.text 节点会拿到已释放的服务
        var appDir = Path.Combine(Path.GetTempPath(), $"easycon-flow-{Guid.NewGuid():N}");
        Directory.CreateDirectory(appDir);
        File.WriteAllText(Path.Combine(appDir, "noop.ecs"), "PRINT \"noop\"\n");

        var ocr = new StubOcr("BULBASAUR");
        var graph = Graph("""
        {
          "nodes": [
            { "id": "start", "type": "start" },
            { "id": "cap", "type": "capture.frame" },
            { "id": "run", "type": "script.run", "params": { "file": "noop.ecs" } },
            { "id": "ocr", "type": "ocr.text",
              "inputs": { "image": { "node": "cap", "port": "image" } } },
            { "id": "end", "type": "end" }
          ],
          "exec": [ ["start", "run"], ["run", "ocr"], ["ocr", "end"] ]
        }
        """);
        var report = new FlowExecutor(graph, Context(frameText: "x", appDir: appDir, ocr: ocr))
            .Run(new CancellationTokenSource().Token);

        Assert.That(report.Error, Is.Null, report.Error ?? "");
        Assert.That(ocr.Disposed, Is.False, "借用语义下 script.run 不得释放外层共享 OCR");
        Assert.That(report.Records["ocr"].LastOutputs["text"], Is.EqualTo("BULBASAUR"));
    }

    // ---- 慢感知与等待新帧 ----

    /// <summary>可控制帧号/内容的自有采集源：每次抓帧消耗一帧（模拟自由运行的采集卡）。</summary>
    private sealed class FakeFrameSource : ICaptureSource
    {
        private long _index;
        private long _pushes;

        public bool ProvideIndex { get; set; } = true;

        /// <summary>每帧内容不同（onChange 判据用）。</summary>
        public bool ChangingPayload { get; set; }

        public int Captures => (int)Interlocked.Read(ref _pushes);

        public long? FrameIndex => ProvideIndex ? Interlocked.Read(ref _index) : null;

        /// <summary>外部推进帧号（模拟采集卡又出了一帧）。</summary>
        public void Push(long count = 1) => Interlocked.Add(ref _index, count);

        public string? CaptureFrame(int x, int y, int width, int height)
        {
            var push = Interlocked.Increment(ref _pushes);
            Interlocked.Increment(ref _index);
            var color = ChangingPayload ? new Scalar(push % 255, 0, 0) : Scalar.Blue;
            using var mat = new Mat(16, 16, MatType.CV_8UC3, color);
            return Convert.ToBase64String(mat.ToBytes(".png"));
        }
    }

    [Test]
    public void Slow_EveryFrames_ReusesUntilFrameBudgetAdvances()
    {
        var source = new FakeFrameSource();
        var graph = Graph("""
        {
          "maxSteps": 8,
          "nodes": [
            { "id": "tick", "type": "capture.frame" },
            { "id": "slow", "type": "capture.frame", "slow": { "everyFrames": 3 } }
          ],
          "exec": [ ["tick", "slow"], ["slow", "tick"] ]
        }
        """);
        var report = new FlowExecutor(graph, new FlowHostContext { Capture = source })
            .Run(new CancellationTokenSource().Token);

        // tick 每步抓帧推进帧号；slow 每 3 帧才算一次 → 8 步里只真跑 2 次
        Assert.Multiple(() =>
        {
            Assert.That(report.Error, Is.Null, report.Error ?? "");
            Assert.That(report.Records["slow"].ExecCount, Is.EqualTo(4));
            Assert.That(report.Records["slow"].ReusedCount, Is.EqualTo(2), "帧号推进不足 3 帧时复用上次输出");
            Assert.That(source.Captures, Is.EqualTo(6), "tick 4 次 + slow 真跑 2 次");
            Assert.That(report.Events.Count(e => e.StartsWith("nodeReuse slow", StringComparison.Ordinal)), Is.EqualTo(2));
        });
    }

    [Test]
    public void Slow_EveryFrames_WithoutFrameIndexNeverReuses()
    {
        // 源不给帧号 → 无法判定新帧 → 保守地每次都执行（fail-open 到「总是重算」）
        var source = new FakeFrameSource { ProvideIndex = false };
        var graph = Graph("""
        {
          "maxSteps": 4,
          "nodes": [ { "id": "cap", "type": "capture.frame", "slow": { "everyFrames": 3 } } ],
          "exec": [ ["cap", "cap"] ]
        }
        """);
        var report = new FlowExecutor(graph, new FlowHostContext { Capture = source })
            .Run(new CancellationTokenSource().Token);

        Assert.That(report.Records["cap"].ReusedCount, Is.EqualTo(0));
        Assert.That(report.Records["cap"].ExecCount, Is.EqualTo(4));
    }

    [Test]
    public void Slow_IntervalMs_ReusesWithinWindow()
    {
        var source = new FakeFrameSource();
        var graph = Graph("""
        {
          "maxSteps": 5,
          "nodes": [ { "id": "cap", "type": "capture.frame", "slow": { "intervalMs": 60000 } } ],
          "exec": [ ["cap", "cap"] ]
        }
        """);
        var report = new FlowExecutor(graph, new FlowHostContext { Capture = source })
            .Run(new CancellationTokenSource().Token);

        Assert.Multiple(() =>
        {
            Assert.That(report.Records["cap"].ExecCount, Is.EqualTo(5));
            Assert.That(report.Records["cap"].ReusedCount, Is.EqualTo(4), "间隔未到 → 复用");
            Assert.That(source.Captures, Is.EqualTo(1));
        });
    }

    [Test]
    public void Slow_OnChange_TracksFrameContent()
    {
        static FlowRunReport RunLoop(FakeFrameSource source) => new FlowExecutor(Graph("""
        {
          "maxSteps": 6,
          "nodes": [ { "id": "cap", "type": "capture.frame", "slow": { "onChange": true } } ],
          "exec": [ ["cap", "cap"] ]
        }
        """), new FlowHostContext { Capture = source }).Run(new CancellationTokenSource().Token);

        FlowRunReport identical = RunLoop(new FakeFrameSource());
        FlowRunReport changing = RunLoop(new FakeFrameSource { ChangingPayload = true });

        Assert.Multiple(() =>
        {
            Assert.That(identical.Records["cap"].ReusedCount, Is.EqualTo(5), "画面未变 → 复用");
            Assert.That(changing.Records["cap"].ReusedCount, Is.EqualTo(0), "画面每帧都变 → 不复用");
        });
    }

    [Test]
    public void CaptureFrame_WaitForNew_ReturnsWhenFrameAdvances()
    {
        var source = new FakeFrameSource();
        // 先耗尽一帧，使首次抓帧后帧号停住；等待期间由外部推帧
        _ = source.CaptureFrame(-1, -1, -1, -1);
        var graph = Graph("""
        {
          "nodes": [
            { "id": "cap", "type": "capture.frame", "params": { "waitForNew": true, "timeoutMs": 5000 } },
            { "id": "end", "type": "end" }
          ],
          "exec": [ ["cap", "end"] ]
        }
        """);
        _ = Task.Run(async () =>
        {
            await Task.Delay(30);
            source.Push();
        });

        var sw = System.Diagnostics.Stopwatch.StartNew();
        var report = new FlowExecutor(graph, new FlowHostContext { Capture = source })
            .Run(new CancellationTokenSource().Token);
        sw.Stop();

        Assert.Multiple(() =>
        {
            Assert.That(report.Error, Is.Null, report.Error ?? "");
            Assert.That(sw.ElapsedMilliseconds, Is.LessThan(3000), "帧号推进后应立即返回");
            Assert.That(report.Records["cap"].LastOutputs["frameIndex"], Is.Not.Null);
        });
    }

    [Test]
    public void CaptureFrame_WaitForNew_TimesOutInsteadOfHanging()
    {
        var source = new FakeFrameSource();
        var graph = Graph("""
        {
          "nodes": [
            { "id": "cap", "type": "capture.frame", "params": { "waitForNew": true, "timeoutMs": 60 } },
            { "id": "end", "type": "end" }
          ],
          "exec": [ ["cap", "end"] ]
        }
        """);

        var sw = System.Diagnostics.Stopwatch.StartNew();
        var report = new FlowExecutor(graph, new FlowHostContext { Capture = source })
            .Run(new CancellationTokenSource().Token);
        sw.Stop();

        Assert.Multiple(() =>
        {
            Assert.That(report.Error, Is.Null, "等不到新帧只降级取当前帧，不算节点错误");
            Assert.That(sw.ElapsedMilliseconds, Is.GreaterThanOrEqualTo(50), "应受 timeoutMs 约束");
            Assert.That(sw.ElapsedMilliseconds, Is.LessThan(3000));
            Assert.That(report.Records["cap"].LastOutputs["image"], Is.Not.Null);
        });
    }

    [Test]
    public void ExecuteGuard_BlocksActuationReachedThroughDataEdge()
    {
        var pad = new StubPad();
        var graph = Graph("""
        {
          "nodes": [
            { "id": "chk", "type": "compare", "params": { "op": "==" },
              "inputs": { "a": { "node": "press", "port": "out" }, "b": 1 } },
            { "id": "press", "type": "pad.key", "params": { "key": "A" } }
          ],
          "exec": [ ["press", "chk"] ]
        }
        """);

        var runtime = new FlowNodeRuntime(graph, Context(pad: pad))
        {
            ExecuteGuard = n => n.Type.StartsWith("pad.", StringComparison.Ordinal) ? "拒绝：试跑不驱动设备" : null,
        };

        var ex = Assert.Throws<InvalidOperationException>(
            () => runtime.Execute(graph.Node("chk")!, CancellationToken.None));
        Assert.Multiple(() =>
        {
            Assert.That(ex!.Message, Does.Contain("拒绝"));
            Assert.That(pad.Clicks, Is.Empty, "守卫生效时设备指令不得真的发出去");
        });
    }

    // ---- 决策层与感知层新增节点 ----

    [Test]
    public void TextContains_BranchesByMode()
    {
        static FlowRunReport Run(string mode, string text, string pattern, bool ignoreCase = true) =>
            new FlowExecutor(Graph($$"""
            {
              "nodes": [
                { "id": "chk", "type": "text.contains",
                  "params": { "mode": "{{mode}}", "pattern": "{{pattern.Replace("\\", "\\\\")}}", "ignoreCase": {{(ignoreCase ? "true" : "false")}} },
                  "inputs": { "text": "{{text}}" } },
                { "id": "yes", "type": "wait", "params": { "ms": 1 } },
                { "id": "no", "type": "wait", "params": { "ms": 1 } }
              ],
              "exec": [ ["chk.true", "yes"], ["chk.false", "no"] ]
            }
            """), Context()).Run(new CancellationTokenSource().Token);

        Assert.Multiple(() =>
        {
            Assert.That(Run("contains", "Met in SAFARI ZONE at Lv25.", "safari").Records.ContainsKey("yes"), Is.True,
                "contains + 忽略大小写");
            Assert.That(Run("startsWith", "Lv25 TAUROS", "Lv25").Records.ContainsKey("yes"), Is.True);
            Assert.That(Run("endsWith", "Lv25 TAUROS", "TAUROS").Records.ContainsKey("yes"), Is.True);
            Assert.That(Run("equals", "Lv25 TAUROS", "lv25 tauros").Records.ContainsKey("yes"), Is.True);
            Assert.That(Run("equals", "Lv25 TAUROS", "lv25 tauros", ignoreCase: false).Records.ContainsKey("no"), Is.True,
                "区分大小写时不相等");
            Assert.That(Run("regex", "PP: 19531", @"\d{4,}").Records.ContainsKey("yes"), Is.True);
            Assert.That(Run("contains", "abc", "zzz").Records.ContainsKey("no"), Is.True);
        });
    }

    [Test]
    public void StateStep_CountsAndSignalsDone()
    {
        // 每次循环 inc 1，达到 3 走 done：验证运行内计数器与双出口
        var graph = Graph("""
        {
          "maxSteps": 20,
          "nodes": [
            { "id": "step", "type": "state.step", "params": { "name": "loop", "op": "inc", "value": 1, "max": 3 } },
            { "id": "body", "type": "wait", "params": { "ms": 1 } },
            { "id": "after", "type": "wait", "params": { "ms": 1 } }
          ],
          "exec": [ ["step.out", "body"], ["body", "step"], ["step.done", "after"] ]
        }
        """);
        var report = new FlowExecutor(graph, Context()).Run(new CancellationTokenSource().Token);

        Assert.Multiple(() =>
        {
            Assert.That(report.Error, Is.Null, report.Error ?? "");
            // step 先跑（1），body 后回跳：第 3 次 step 时 value 达 3 走 done
            Assert.That(report.Records["step"].ExecCount, Is.EqualTo(3), "2 次未达上限 + 1 次达上限");
            Assert.That(report.Records["body"].ExecCount, Is.EqualTo(2));
            Assert.That(report.Records["step"].LastOutputs["value"], Is.EqualTo(3L));
            Assert.That(report.Records["after"].ExecCount, Is.EqualTo(1));
        });
    }

    [Test]
    public void StateStep_SupportsSetResetAndRejectsInvalidOp()
    {
        var graph = Graph("""
        {
          "nodes": [
            { "id": "set",  "type": "state.step", "params": { "name": "n", "op": "set", "value": 5 } },
            { "id": "rst",  "type": "state.step", "params": { "name": "n", "op": "reset" } },
            { "id": "end",  "type": "end" }
          ],
          "exec": [ ["set", "rst"], ["rst", "end"] ]
        }
        """);
        var report = new FlowExecutor(graph, Context()).Run(new CancellationTokenSource().Token);
        Assert.Multiple(() =>
        {
            Assert.That(report.Records["set"].LastOutputs["value"], Is.EqualTo(5L));
            Assert.That(report.Records["rst"].LastOutputs["value"], Is.EqualTo(0L));
        });

        var bad = Graph("""
        {
          "nodes": [ { "id": "s", "type": "state.step", "params": { "op": "bogus" } } ],
          "exec": []
        }
        """);
        var badReport = new FlowExecutor(bad, Context()).Run(new CancellationTokenSource().Token);
        Assert.That(badReport.Error, Does.Contain("op"));
    }

    [Test]
    public void VisionChanged_FirstRunIsBaselineThenTracksChanges()
    {
        var graph = Graph("""
        {
          "maxSteps": 6,
          "nodes": [
            { "id": "chg", "type": "vision.changed" },
            { "id": "loop", "type": "wait", "params": { "ms": 1 } }
          ],
          "exec": [ ["chg.true", "loop"], ["chg.false", "loop"], ["loop", "chg"] ]
        }
        """);

        var changing = new FlowExecutor(graph, new FlowHostContext { Capture = new FakeFrameSource { ChangingPayload = true } })
            .Run(new CancellationTokenSource().Token);
        var steady = new FlowExecutor(graph, new FlowHostContext { Capture = new FakeFrameSource() })
            .Run(new CancellationTokenSource().Token);

        Assert.Multiple(() =>
        {
            Assert.That(changing.Error, Is.Null, changing.Error ?? "");
            Assert.That(changing.Records["chg"].ExecCount, Is.EqualTo(3), "6 步 = 3 次 chg + 3 次 loop");
            Assert.That(changing.Records["chg"].LastOutputs["changed"], Is.EqualTo(1), "画面逐帧变化 → 第二次起为 1");
            Assert.That(steady.Records["chg"].LastOutputs["changed"], Is.EqualTo(0), "画面不变（含首次基线）恒为 0");
        });
    }

    [Test]
    public void StopToken_AbortsRun()
    {
        var graph = Graph("""
        {
          "nodes": [ { "id": "a", "type": "wait", "params": { "ms": 1 } } ],
          "exec": [ ["a", "a"] ]
        }
        """);
        var cts = new CancellationTokenSource();
        cts.Cancel();
        Assert.Throws<OperationCanceledException>(
            () => new FlowExecutor(graph, Context()).Run(cts.Token));
    }
}