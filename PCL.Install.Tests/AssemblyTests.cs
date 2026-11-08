namespace PCL.Install.Tests;

using PCL.Download;

public sealed class AssemblyTests
{
    private static InstallPlanRequest VanillaRequest(bool hasJson) => new()
    {
        MinecraftVersion = "1.20.1",
        HasMinecraftJson = hasJson,
    };

    private static InstallActionRegistry StubActions() => new()
    {
        Fallback = new RecordingAction(new List<string>()),
    };

    private static InstallAssembler Assembler(
        InstallPlanRequest request,
        INetFileSource? files = null,
        InstallActionRegistry? actions = null,
        SubStepProviderRegistry? subSteps = null)
        => new(request, new AllPresentAssets(), files ?? new FakeFileSource(),
            actions ?? StubActions(), subSteps ?? new SubStepProviderRegistry());

    [Fact]
    public void 装配产物与P4A描述符逐项一致且顺序不变()
    {
        var request = VanillaRequest(hasJson: false);
        var plan = InstallPlanner.Build(request);
        var nodes = Assembler(request).Assemble(plan);

        Assert.Equal(plan.Steps.Count, nodes.Count);
        for (var i = 0; i < plan.Steps.Count; i++)
        {
            var step = plan.Steps[i];
            var node = nodes[i];

            Assert.Same(step.Task, node.Descriptor);
            Assert.Equal(step.Kind, node.Kind);
            Assert.Equal(step.Name, node.Name);
            Assert.Equal(step.Task.LoaderKind, node.LoaderKind);
            Assert.Equal(step.ProgressWeight, node.ProgressWeight);
            Assert.Equal(step.Block, node.Block);
            Assert.Equal(step.Show, node.Show);
            Assert.Equal(step.Task.Cancel, node.Cancel);
            Assert.Equal(step.ReportsProgress, node.ReportsProgress);
            Assert.Equal(step.Inputs, node.Inputs);
            Assert.Equal(step.Outputs, node.Outputs);
            Assert.Equal(step.DownloadKinds, node.DownloadKinds);
        }
    }

    [Fact]
    public void 客户端复合体子步骤与P4A骨架一一对应()
    {
        var request = VanillaRequest(hasJson: false);
        var plan = InstallPlanner.Build(request);
        var nodes = Assembler(request).Assemble(plan);

        var client = nodes.Single(n => n.Kind == InstallStepKind.Client);
        Assert.Equal(client.Descriptor.SubSteps.Count, client.Children.Count);
        Assert.Equal(
            client.Descriptor.SubSteps.Select(s => s.Name),
            client.Children.Select(c => c.Name));
    }

    [Fact]
    public void 提供HasMinecraftJson时客户端子步骤少一步()
    {
        var withoutJson = InstallPlanner.Build(VanillaRequest(hasJson: false));
        var clientWithout = Assembler(VanillaRequest(false)).Assemble(withoutJson)
            .Single(n => n.Kind == InstallStepKind.Client);

        var withJson = InstallPlanner.Build(VanillaRequest(hasJson: true));
        var clientWith = Assembler(VanillaRequest(true)).Assemble(withJson)
            .Single(n => n.Kind == InstallStepKind.Client);

        Assert.Equal(clientWithout.Children.Count - 1, clientWith.Children.Count);
    }

    [Fact]
    public void 缺执行实现时装配立即抛出()
    {
        var request = VanillaRequest(hasJson: false);
        var plan = InstallPlanner.Build(request);
        var assembler = new InstallAssembler(
            request, new AllPresentAssets(), new FakeFileSource(), new InstallActionRegistry());

        var ex = Assert.Throws<MissingInstallActionException>(() => assembler.Assemble(plan));
        Assert.False(string.IsNullOrEmpty(ex.StepName));
    }

    [Fact]
    public void 缺下载清单时装配立即抛出()
    {
        var request = VanillaRequest(hasJson: false);
        var plan = InstallPlanner.Build(request);
        var assembler = Assembler(request, files: new FakeFileSource { ReturnNull = true });

        Assert.Throws<MissingNetFileSourceException>(() => assembler.Assemble(plan));
    }

    [Fact]
    public void 宿主文件类别与声明不符时装配立即抛出()
    {
        var request = VanillaRequest(hasJson: false);
        var plan = InstallPlanner.Build(request);
        var assembler = Assembler(request, files: new FakeFileSource { ForceFabricMain = true });

        var ex = Assert.Throws<NetFileKindMismatchException>(() => assembler.Assemble(plan));
        Assert.False(string.IsNullOrEmpty(ex.StepName));
    }

    [Fact]
    public void 未注册子步骤提供者时装配立即抛出()
    {
        var request = new InstallPlanRequest
        {
            MinecraftVersion = "1.20.1",
            HasAnyModLoader = true,
            HasForgeEntry = true,
            ForgeVersion = "47.2.0",
        };
        var plan = InstallPlanner.Build(request);

        var ex = Assert.Throws<MissingSubStepSourceException>(() => Assembler(request).Assemble(plan));
        Assert.Contains("Forgelike", ex.SubStepsSource);
    }

    [Fact]
    public void 注册子步骤提供者后复合体可装配()
    {
        var request = new InstallPlanRequest
        {
            MinecraftVersion = "1.20.1",
            HasAnyModLoader = true,
            HasForgeEntry = true,
            ForgeVersion = "47.2.0",
        };
        var plan = InstallPlanner.Build(request);

        var missing = Assert.Throws<MissingSubStepSourceException>(() => Assembler(request).Assemble(plan));
        var source = missing.SubStepsSource!;

        var subStep = InstallTaskDescriptors.Create(
            InstallStepKind.FabricApiDownload, "子步骤-下载依赖", 1, block: false, show: true, request);
        var subSteps = new SubStepProviderRegistry().Register(source, new FixedSubStepProvider([subStep]));
        var nodes = Assembler(request, subSteps: subSteps).Assemble(plan);

        var forge = nodes.Single(n => n.Kind == InstallStepKind.Forge);
        Assert.Single(forge.Children);
        Assert.Equal("子步骤-下载依赖", forge.Children[0].Name);
    }
}

internal sealed class FixedSubStepProvider : ISubStepProvider
{
    private readonly IReadOnlyList<InstallTaskDescriptor> _subSteps;

    public FixedSubStepProvider(IReadOnlyList<InstallTaskDescriptor> subSteps) => _subSteps = subSteps;

    public IReadOnlyList<InstallTaskDescriptor> Describe(InstallPlanRequest request) => _subSteps;
}
