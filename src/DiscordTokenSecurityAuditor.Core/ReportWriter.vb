Option Strict On
Option Explicit On
Option Infer On

Imports System
Imports System.Collections.Generic
Imports System.IO
Imports System.Text.Json

Namespace DiscordTokenSecurityAuditor.Core
    Public NotInheritable Class ReportWriter
        Private Sub New()
        End Sub

        Public Shared Sub SaveJson(reportPath As String, result As ScanResult)
            If String.IsNullOrWhiteSpace(reportPath) Then
                Throw New ArgumentException("A report path is required.", NameOf(reportPath))
            End If

            If result Is Nothing Then
                Throw New ArgumentNullException(NameOf(result))
            End If

            Dim safeReport As New SafeReport With {
                .GeneratedAtUtc = DateTimeOffset.UtcNow,
                .FilesScanned = result.FilesScanned,
                .FilesSkipped = result.FilesSkipped,
                .ElapsedMilliseconds = result.ElapsedMilliseconds,
                .Findings = result.Findings,
                .Warnings = result.Warnings
            }

            Dim options As New JsonSerializerOptions With {
                .WriteIndented = True
            }

            Dim fullPath As String = Path.GetFullPath(reportPath)
            Dim parent As String = Path.GetDirectoryName(fullPath)
            If Not String.IsNullOrWhiteSpace(parent) Then
                Directory.CreateDirectory(parent)
            End If

            File.WriteAllText(fullPath, JsonSerializer.Serialize(safeReport, options))
        End Sub

        Private Class SafeReport
            Public Property GeneratedAtUtc As DateTimeOffset
            Public Property FilesScanned As Integer
            Public Property FilesSkipped As Integer
            Public Property ElapsedMilliseconds As Long
            Public Property Findings As List(Of ScanFinding) = New List(Of ScanFinding)()
            Public Property Warnings As List(Of String) = New List(Of String)()
        End Class
    End Class
End Namespace
