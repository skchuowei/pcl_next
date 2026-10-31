Public Class PageOtherAbout

    Private Shadows IsLoaded As Boolean = False
    Private Sub PageOtherAbout_Loaded(sender As Object, e As RoutedEventArgs) Handles Me.Loaded

        '重复加载部分
        PanBack.ScrollToHome()

        '非重复加载部分
        If IsLoaded Then Return
        IsLoaded = True

        ItemAboutPcl.Info = ItemAboutPcl.Info.Replace("%VERSION%", VersionDisplay).Replace("%VERSIONCODE%", VersionCode).Replace("%BRANCH%", CInt(BuildType))
        '开发版不展示上游 PCL 的赞助入口——赞助属于上游作者，与本项目无关
        If ModBuildChannel.ChannelIsInternal Then
            BtnDonateDonate.Visibility = Visibility.Collapsed
            BtnDonateOutput.Visibility = Visibility.Visible
        End If

    End Sub

    Public Shared Sub CopyIdentify() Handles BtnDonateCopy.Click
        ClipboardSet(Identify)
    End Sub
    Private Sub BtnDonateCodeInput_Click() Handles BtnDonateInput.Click
        InputPotatoCode(False)
    End Sub
    Private Sub BtnDonateOutput_Click(sender As Object, e As EventArgs) Handles BtnDonateOutput.Click
        GeneratePotatoCode()
    End Sub
    Private Sub BtnAboutUpdate_Click(sender As Object, e As MouseButtonEventArgs) Handles BtnAboutUpdate.Click
        UpdateCheckByButton()
    End Sub

End Class
