'pcl-next 新增：.NET 10 不再提供 Microsoft.VisualBasic 的 My 命名空间（那是桌面框架专属的生成代码），
'这里按 .NET Framework 中 My.Computer / My.Application 的语义补齐 PCL 实际用到的那部分成员，
'以便其余业务代码保持零改动。

Imports System.Runtime.InteropServices

Namespace My

    ''' <summary>
    ''' 等价于 My.Application 中被使用的成员。
    ''' </summary>
    Friend Module Application

        ''' <summary>
        ''' 在当前应用的资源字典中查找资源，等价于 Application.TryFindResource。
        ''' </summary>
        Friend Function TryFindResource(ResourceKey As Object) As Object
            Dim CurrentApplication = Global.System.Windows.Application.Current
            If CurrentApplication Is Nothing Then Return Nothing
            Return CurrentApplication.TryFindResource(ResourceKey)
        End Function

    End Module

    ''' <summary>
    ''' 等价于 My.Computer 中被使用的成员。
    ''' </summary>
    Friend Module Computer

        Friend ReadOnly Property Info As New ComputerInfo
        Friend ReadOnly Property Clipboard As New ComputerClipboard
        Friend ReadOnly Property Screen As New ComputerScreen
        Friend ReadOnly Property Keyboard As New ComputerKeyboard
        Friend ReadOnly Property Clock As New ComputerClock

    End Module

    ''' <summary>
    ''' 系统信息，对应 My.Computer.Info。
    ''' </summary>
    Friend NotInheritable Class ComputerInfo

        ''' <summary>
        ''' 当前可用的物理内存（字节）。
        ''' </summary>
        Friend ReadOnly Property AvailablePhysicalMemory As ULong
            Get
                Return QueryMemory().ullAvailPhys
            End Get
        End Property

        ''' <summary>
        ''' 物理内存总量（字节）。
        ''' </summary>
        Friend ReadOnly Property TotalPhysicalMemory As ULong
            Get
                Return QueryMemory().ullTotalPhys
            End Get
        End Property

        ''' <summary>
        ''' 系统版本字符串，对应 Environment.OSVersion.VersionString。
        ''' </summary>
        Friend ReadOnly Property OSVersion As String
            Get
                Return Environment.OSVersion.VersionString
            End Get
        End Property

        ''' <summary>
        ''' 系统完整名称，用于反馈信息展示。
        ''' </summary>
        Friend ReadOnly Property OSFullName As String
            Get
                Return If(RuntimeInformation.OSDescription, OSVersion)
            End Get
        End Property

        Private Shared Function QueryMemory() As MEMORYSTATUSEX
            Dim Status As New MEMORYSTATUSEX With {.dwLength = CUInt(Marshal.SizeOf(GetType(MEMORYSTATUSEX)))}
            If Not MyNative.GlobalMemoryStatusEx(Status) Then Return Nothing
            Return Status
        End Function

    End Class

    ''' <summary>
    ''' 剪贴板，对应 My.Computer.Clipboard（WPF 实现）。
    ''' </summary>
    Friend NotInheritable Class ComputerClipboard

        Friend Sub Clear()
            Global.System.Windows.Clipboard.Clear()
        End Sub

        Friend Sub SetText(Text As String)
            Global.System.Windows.Clipboard.SetText(Text)
        End Sub

        Friend Function ContainsText() As Boolean
            Return Global.System.Windows.Clipboard.ContainsText()
        End Function

        Friend Function GetText() As String
            Return Global.System.Windows.Clipboard.GetText()
        End Function

    End Class

    ''' <summary>
    ''' 主屏幕，对应 My.Computer.Screen。单位为物理像素，与 .NET Framework 的实现一致。
    ''' </summary>
    Friend NotInheritable Class ComputerScreen

        Private Const SPI_GETWORKAREA As UInteger = &H30

        Friend NotInheritable Class ScreenInfo
            Friend ReadOnly Property WorkingArea As Global.System.Drawing.Rectangle
                Get
                    Dim Area As RECT
                    If Not MyNative.SystemParametersInfo(SPI_GETWORKAREA, 0, Area, 0) Then Return Global.System.Windows.Forms.Screen.PrimaryScreen.WorkingArea
                    Return New Global.System.Drawing.Rectangle(Area.Left, Area.Top, Area.Right - Area.Left, Area.Bottom - Area.Top)
                End Get
            End Property
        End Class

        Private ReadOnly Primary As New ScreenInfo

        ''' <summary>
        ''' 主屏幕的工作区（物理像素）。
        ''' </summary>
        Friend ReadOnly Property WorkingArea As Global.System.Drawing.Rectangle
            Get
                Return Primary.WorkingArea
            End Get
        End Property

    End Class

    ''' <summary>
    ''' 键盘状态，对应 My.Computer.Keyboard。
    ''' </summary>
    Friend NotInheritable Class ComputerKeyboard

        Private Const VK_SHIFT As Integer = &H10
        Private Const VK_CONTROL As Integer = &H11

        Friend ReadOnly Property CtrlKeyDown As Boolean
            Get
                Return IsKeyDown(VK_CONTROL)
            End Get
        End Property

        Friend ReadOnly Property ShiftKeyDown As Boolean
            Get
                Return IsKeyDown(VK_SHIFT)
            End Get
        End Property

        Private Shared Function IsKeyDown(VirtualKey As Integer) As Boolean
            Return MyNative.GetKeyState(VirtualKey) < 0
        End Function

    End Class

    ''' <summary>
    ''' 时钟，对应 My.Computer.Clock。
    ''' </summary>
    Friend NotInheritable Class ComputerClock

        ''' <summary>
        ''' 系统启动以来的毫秒数。
        ''' </summary>
        Friend ReadOnly Property TickCount As Integer
            Get
                Return Environment.TickCount
            End Get
        End Property

    End Class

    <StructLayout(LayoutKind.Sequential)>
    Friend Structure MEMORYSTATUSEX
        Friend dwLength As UInteger
        Friend dwMemoryLoad As UInteger
        Friend ullTotalPhys As ULong
        Friend ullAvailPhys As ULong
        Friend ullTotalPageFile As ULong
        Friend ullAvailPageFile As ULong
        Friend ullTotalVirtual As ULong
        Friend ullAvailVirtual As ULong
        Friend ullAvailExtendedVirtual As ULong
    End Structure

    <StructLayout(LayoutKind.Sequential)>
    Friend Structure RECT
        Friend Left As Integer
        Friend Top As Integer
        Friend Right As Integer
        Friend Bottom As Integer
    End Structure

    ''' <summary>
    ''' 供 MyCompat 使用的 Win32 调用。
    ''' </summary>
    Friend Module MyNative

        <DllImport("kernel32.dll", SetLastError:=True)>
        Friend Function GlobalMemoryStatusEx(ByRef Buffer As MEMORYSTATUSEX) As Boolean
        End Function

        <DllImport("user32.dll", SetLastError:=True)>
        Friend Function SystemParametersInfo(Action As UInteger, Param As UInteger, ByRef Data As RECT, WinIni As UInteger) As Boolean
        End Function

        <DllImport("user32.dll")>
        Friend Function GetKeyState(VirtualKey As Integer) As Short
        End Function

    End Module

End Namespace
