Option Strict On
Option Explicit On
Option Infer On

Imports System
Imports System.Collections.Generic

Namespace DiscordTokenSecurityAuditor.Core
    Public Class ScanFinding
        Public Property Severity As String = String.Empty
        Public Property Category As String = String.Empty
        Public Property FilePath As String = String.Empty
        Public Property LineNumber As Integer?
        Public Property Source As String = String.Empty
        Public Property Recommendation As String = String.Empty
    End Class

    Public Class ScanProgress
        Public Property FilesScanned As Integer
        Public Property CurrentPath As String = String.Empty
    End Class

    Public Class ScanResult
        Public Property Findings As List(Of ScanFinding) = New List(Of ScanFinding)()
        Public Property Warnings As List(Of String) = New List(Of String)()
        Public Property FilesScanned As Integer
        Public Property FilesSkipped As Integer
        Public Property ElapsedMilliseconds As Long
    End Class
End Namespace
