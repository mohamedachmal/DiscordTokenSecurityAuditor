Option Strict On
Option Explicit On
Option Infer On

Imports System
Imports System.Windows.Forms

Namespace DiscordTokenSecurityAuditor.Desktop
    Friend Module Program
        <STAThread>
        Public Sub Main()
            Application.EnableVisualStyles()
            Application.SetCompatibleTextRenderingDefault(False)
            Application.Run(New MainForm())
        End Sub
    End Module
End Namespace
