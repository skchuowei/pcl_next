using Xunit;

namespace PCL.Download.Tests;

/// <summary>
/// 资源模型测试集合：ResourceProject / ResourceVersion 含静态内存缓存（与 VB 语义一致），
/// 各测试类的构造函数会清空它们，因此相关测试类必须串行执行。
/// </summary>
[CollectionDefinition("Resource", DisableParallelization = true)]
public class ResourceTestCollection;
