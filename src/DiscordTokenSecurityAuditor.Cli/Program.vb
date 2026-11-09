Option Strict On
Option Explicit On
Option Infer On

Imports System
Imports System.IO
Imports DiscordTokenSecurityAuditor.Core

Namespace DiscordTokenSecurityAuditor.Cli
    Friend Module Program
        Public Function Main(args As String()) As Integer
            If args.Length = 0 OrElse String.Equals(args(0), "--help", StringComparison.OrdinalIgnoreCase) OrElse String.Equals(args(0), "-h", StringComparison.OrdinalIgnoreCase) Then
                PrintUsage()
                Return 0
            End If

            If Not String.Equals(args(0), "scan", StringComparison.OrdinalIgnoreCase) OrElse args.Length < 2 Then
                PrintUsage()
                Return 2
            End If

            Dim targetPath As String = args(1)
            Dim includeHistory As Boolean = False
            Dim jsonPath As String = Nothing

            Dim index As Integer = 2
            While index < args.Length
                Select Case args(index).ToLowerInvariant()
                    Case "--history"
                        includeHistory = True
                    Case "--json"
                        If index + 1 >= args.Length Then
                            Console.Error.WriteLine("Missing file path after --json.")
                            Return 2
                        End If
                        index += 1
                        jsonPath = args(index)
                    Case Else
                        Console.Error.WriteLine("Unknown option: " & args(index))
                        PrintUsage()
                        Return 2
                End Select
                index += 1
            End While

            If Not Directory.Exists(targetPath) Then
                Console.Error.WriteLine("The selected folder does not exist or is not accessible.")
                Return 2
            End If

            Try
                Console.WriteLine("Discord Token Security Auditor")
                Console.WriteLine("Local scan only. Secret values are never printed or included in reports.")
                Console.WriteLine()

                Dim result As ScanResult = TokenScanner.ScanAsync(targetPath, includeHistory).GetAwaiter().GetResult()
                Console.WriteLine("Files visited: {0} | skipped: {1} | elapsed: {2:N1}s", result.FilesScanned, result.FilesSkipped, result.ElapsedMilliseconds / 1000.0R)
                Console.WriteLine("Potential findings: " & result.Findings.Count.ToString())
                Console.WriteLine()

                For Each finding As ScanFinding In result.Findings
                    Dim lineText As String = If(finding.LineNumber.HasValue, ":" & finding.LineNumber.Value.ToString(), String.Empty)
                    Console.WriteLine("[{0}] {1} | {2}{3} | {4} | {5}",
                                      finding.Severity,
                                      finding.Source,
                                      finding.FilePath,
                                      lineText,
                                      finding.Category,
                                      finding.Recommendation)
                Next

                For Each warning As String In result.Warnings
                    Console.WriteLine("[Warning] " & warning)
                Next

                If Not String.IsNullOrWhiteSpace(jsonPath) Then
                    ReportWriter.SaveJson(jsonPath, result)
                    Console.WriteLine()
                    Console.WriteLine("Sanitized report saved to: " & Path.GetFullPath(jsonPath))
                End If

                Return If(result.Findings.Count > 0, 1, 0)
            Catch ex As OperationCanceledException
                Console.Error.WriteLine("Scan cancelled.")
                Return 130
            Catch
                Console.Error.WriteLine("The scan could not complete. Check the folder permissions and try again.")
                Return 2
            End Try
        End Function

        Private Sub PrintUsage()
            Console.WriteLine("Usage:")
            Console.WriteLine("  DiscordTokenSecurityAuditor.Cli scan <folder> [--history] [--json <report.json>]")
            Console.WriteLine()
            Console.WriteLine("Options:")
            Console.WriteLine("  --history       Inspect added lines in all reachable Git branches/tags.")
            Console.WriteLine("  --json <path>   Export sanitized finding metadata; no secret values are included.")
        End Sub
    End Module
End Namespace
