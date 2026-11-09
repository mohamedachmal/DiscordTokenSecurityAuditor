Option Strict On
Option Explicit On
Option Infer On

Imports System
Imports System.Collections.Generic
Imports System.Diagnostics
Imports System.IO
Imports System.Linq
Imports System.Text
Imports System.Text.RegularExpressions
Imports System.Threading
Imports System.Threading.Tasks

Namespace DiscordTokenSecurityAuditor.Core
    Public NotInheritable Class TokenScanner
        Private Const MaxFileBytes As Long = 2L * 1024L * 1024L
        Private Const MaxLineCharacters As Integer = 262144
        Private Const ProgressInterval As Integer = 25

        Private Shared ReadOnly DiscordTokenPattern As New Regex(
            "(?<![A-Za-z0-9_-])[A-Za-z0-9_-]{20,}\.[A-Za-z0-9_-]{6}\.[A-Za-z0-9_-]{20,}(?![A-Za-z0-9_-])",
            RegexOptions.Compiled Or RegexOptions.CultureInvariant)

        Private Shared ReadOnly DiscordAssignmentPattern As New Regex(
            "(?i)[""']?\b(?<name>DISCORD(?:_BOT)?_TOKEN|BOT_TOKEN)\b[""']?\s*[:=]\s*(?<value>[^#;\r\n,]+)",
            RegexOptions.Compiled Or RegexOptions.CultureInvariant)

        Private Shared ReadOnly TokenLoggingPattern As New Regex(
            "(?i)\b(?:print|console\.log|console\.writeline|debug\.writeline|logger\.(?:trace|debug|info|warn|warning|error|fatal)|logging\.(?:debug|info|warning|error))\s*\([^)]*\b(?<name>DISCORD(?:_BOT)?_TOKEN|BOT_TOKEN)\b",
            RegexOptions.Compiled Or RegexOptions.CultureInvariant)

        Private Shared ReadOnly HunkPattern As New Regex(
            "^@@ -\d+(?:,\d+)? \+(?<line>\d+)(?:,\d+)? @@",
            RegexOptions.Compiled Or RegexOptions.CultureInvariant)

        Private Shared ReadOnly ExcludedDirectories As New HashSet(Of String)(StringComparer.OrdinalIgnoreCase) From {
            ".git", ".svn", ".hg", "node_modules", "packages", "bin", "obj", ".vs", ".idea",
            ".venv", "venv", "vendor", "dist", "build", "target", "coverage",
            ".next", ".nuxt", "__pycache__", ".tox", ".pytest_cache"
        }

        Private Shared ReadOnly ExcludedExtensions As New HashSet(Of String)(StringComparer.OrdinalIgnoreCase) From {
            ".png", ".jpg", ".jpeg", ".gif", ".bmp", ".ico", ".webp", ".tif", ".tiff",
            ".pdf", ".zip", ".7z", ".rar", ".gz", ".tar", ".dll", ".exe", ".so", ".dylib",
            ".db", ".sqlite", ".sqlite3", ".mp4", ".mov", ".mp3", ".wav", ".woff", ".woff2",
            ".ttf", ".otf", ".class", ".jar", ".pyc", ".pyo", ".bin", ".iso", ".lockb"
        }

        Private Sub New()
        End Sub

        Public Shared Async Function ScanAsync(
            rootPath As String,
            includeGitHistory As Boolean,
            Optional progress As IProgress(Of ScanProgress) = Nothing,
            Optional cancellationToken As Threading.CancellationToken = Nothing
        ) As Task(Of ScanResult)
            If String.IsNullOrWhiteSpace(rootPath) Then
                Throw New ArgumentException("A folder path is required.", NameOf(rootPath))
            End If

            Dim fullRoot As String = Path.GetFullPath(rootPath)
            If Not Directory.Exists(fullRoot) Then
                Throw New DirectoryNotFoundException("The selected folder does not exist.")
            End If

            Return Await Task.Run(
                Function() ScanInternal(fullRoot, includeGitHistory, progress, cancellationToken),
                cancellationToken).ConfigureAwait(False)
        End Function

        Private Shared Function ScanInternal(
            rootPath As String,
            includeGitHistory As Boolean,
            progress As IProgress(Of ScanProgress),
            cancellationToken As Threading.CancellationToken
        ) As ScanResult
            Dim timer As Stopwatch = Stopwatch.StartNew()
            Dim result As New ScanResult()
            Dim seen As New HashSet(Of String)(StringComparer.OrdinalIgnoreCase)

            Dim isGitRepository As Boolean = Directory.Exists(Path.Combine(rootPath, ".git")) OrElse File.Exists(Path.Combine(rootPath, ".git"))
            Dim trackedFiles As HashSet(Of String) = New HashSet(Of String)(StringComparer.OrdinalIgnoreCase)

            If isGitRepository Then
                trackedFiles = GetTrackedFiles(rootPath, cancellationToken)
                For Each trackedPath As String In trackedFiles
                    If IsSensitiveEnvironmentFile(trackedPath) Then
                        AddFinding(result, seen, New ScanFinding With {
                            .Severity = "Medium",
                            .Category = "Sensitive environment file tracked by Git",
                            .FilePath = trackedPath,
                            .LineNumber = Nothing,
                            .Source = "Git index",
                            .Recommendation = "Remove the file from version control, rotate any credentials it contained, and keep local environment files out of future commits."
                        })
                    End If
                Next
            End If

            Dim pendingDirectories As New Stack(Of String)()
            pendingDirectories.Push(rootPath)

            While pendingDirectories.Count > 0
                cancellationToken.ThrowIfCancellationRequested()
                Dim currentDirectory As String = pendingDirectories.Pop()

                Try
                    For Each directoryPath As String In Directory.EnumerateDirectories(currentDirectory, "*", SearchOption.TopDirectoryOnly)
                        cancellationToken.ThrowIfCancellationRequested()
                        Dim directoryName As String = Path.GetFileName(directoryPath)
                        If ExcludedDirectories.Contains(directoryName) Then
                            result.FilesSkipped += 1
                            Continue For
                        End If

                        Try
                            If (File.GetAttributes(directoryPath) And FileAttributes.ReparsePoint) <> FileAttributes.ReparsePoint Then
                                pendingDirectories.Push(directoryPath)
                            Else
                                result.FilesSkipped += 1
                            End If
                        Catch
                            result.FilesSkipped += 1
                        End Try
                    Next
                Catch
                    result.FilesSkipped += 1
                End Try

                Try
                    For Each filePath As String In Directory.EnumerateFiles(currentDirectory, "*", SearchOption.TopDirectoryOnly)
                        cancellationToken.ThrowIfCancellationRequested()
                        result.FilesScanned += 1
                        Dim relativePath As String = ToRelativePath(rootPath, filePath)

                        If result.FilesScanned Mod ProgressInterval = 0 Then
                            progress?.Report(New ScanProgress With {
                                .FilesScanned = result.FilesScanned,
                                .CurrentPath = relativePath
                            })
                        End If

                        If Not ShouldInspectFile(filePath) Then
                            result.FilesSkipped += 1
                            Continue For
                        End If

                        ScanCurrentFile(filePath, relativePath, result, seen, cancellationToken)
                    Next
                Catch
                    result.FilesSkipped += 1
                End Try
            End While

            If includeGitHistory Then
                If isGitRepository Then
                    ScanGitHistory(rootPath, result, seen, progress, cancellationToken)
                Else
                    result.Warnings.Add("Git history was requested, but the selected folder is not a Git repository.")
                End If
            End If

            timer.Stop()
            result.ElapsedMilliseconds = timer.ElapsedMilliseconds
            progress?.Report(New ScanProgress With {
                .FilesScanned = result.FilesScanned,
                .CurrentPath = "Scan complete"
            })
            Return result
        End Function

        Private Shared Function GetTrackedFiles(rootPath As String, cancellationToken As Threading.CancellationToken) As HashSet(Of String)
            Dim tracked As New HashSet(Of String)(StringComparer.OrdinalIgnoreCase)
            Try
                Dim startInfo As New ProcessStartInfo("git") With {
                    .WorkingDirectory = rootPath,
                    .UseShellExecute = False,
                    .RedirectStandardOutput = True,
                    .RedirectStandardError = True,
                    .CreateNoWindow = True
                }
                startInfo.ArgumentList.Add("-c")
                startInfo.ArgumentList.Add("core.fsmonitor=false")
                startInfo.ArgumentList.Add("--no-pager")
                startInfo.ArgumentList.Add("ls-files")
                startInfo.ArgumentList.Add("-z")
                startInfo.Environment("GIT_PAGER") = "cat"
                startInfo.Environment("GIT_OPTIONAL_LOCKS") = "0"

                Using process As New Process With {.StartInfo = startInfo}
                    If Not process.Start() Then Return tracked
                    Dim errorTask As Task(Of String) = process.StandardError.ReadToEndAsync()
                    Dim output As String = process.StandardOutput.ReadToEnd()
                    process.WaitForExit()
                    cancellationToken.ThrowIfCancellationRequested()

                    If process.ExitCode = 0 Then
                        For Each item As String In output.Split(ChrW(0), StringSplitOptions.RemoveEmptyEntries)
                            tracked.Add(item.Replace("\", "/"))
                        Next
                    End If

                    ' Consume stderr without displaying it; it may contain repository metadata.
                    Dim ignoredError As String = errorTask.GetAwaiter().GetResult()
                End Using
            Catch ex As OperationCanceledException
                Throw
            Catch
                ' Git is optional. A working-tree scan can still complete without it.
            End Try
            Return tracked
        End Function

        Private Shared Function ShouldInspectFile(filePath As String) As Boolean
            Try
                Dim info As New FileInfo(filePath)
                If Not info.Exists OrElse info.Length > MaxFileBytes Then Return False
                If (File.GetAttributes(filePath) And FileAttributes.ReparsePoint) = FileAttributes.ReparsePoint Then Return False
                If ExcludedExtensions.Contains(info.Extension) Then Return False
                Return True
            Catch
                Return False
            End Try
        End Function

        Private Shared Sub ScanCurrentFile(
            fullPath As String,
            relativePath As String,
            result As ScanResult,
            seen As HashSet(Of String),
            cancellationToken As Threading.CancellationToken
        )
            Try
                Using stream As New FileStream(fullPath, FileMode.Open, FileAccess.Read, FileShare.ReadWrite Or FileShare.Delete)
                    If LooksBinary(stream) Then
                        result.FilesSkipped += 1
                        Return
                    End If

                    stream.Position = 0
                    Using reader As New StreamReader(stream, Encoding.UTF8, True, 4096, False)
                        Dim lineNumber As Integer = 0
                        While True
                            cancellationToken.ThrowIfCancellationRequested()
                            Dim line As String = reader.ReadLine()
                            If line Is Nothing Then Exit While
                            lineNumber += 1
                            If line.Length > MaxLineCharacters Then Continue While

                            Dim detection As DetectionInfo = DetectLine(line, relativePath)
                            If detection IsNot Nothing Then
                                AddFinding(result, seen, New ScanFinding With {
                                    .Severity = detection.Severity,
                                    .Category = detection.Category,
                                    .FilePath = relativePath,
                                    .LineNumber = lineNumber,
                                    .Source = "Working tree",
                                    .Recommendation = detection.Recommendation
                                })
                            End If
                        End While
                    End Using
                End Using
            Catch ex As OperationCanceledException
                Throw
            Catch
                result.FilesSkipped += 1
            End Try
        End Sub

        Private Shared Function LooksBinary(stream As Stream) As Boolean
            Dim buffer(4095) As Byte
            Dim bytesRead As Integer = stream.Read(buffer, 0, buffer.Length)
            For index As Integer = 0 To bytesRead - 1
                If buffer(index) = 0 Then Return True
            Next
            Return False
        End Function

        Private Shared Function DetectLine(line As String, Optional contextPath As String = Nothing) As DetectionInfo
            If DiscordTokenPattern.IsMatch(line) Then
                Return New DetectionInfo With {
                    .Severity = "High",
                    .Category = "Discord token-like value",
                    .Recommendation = "Treat this as compromised if genuine: reset the bot token, replace it in every deployment, and remove the exposed value from the working tree and repository history."
                }
            End If

            Dim loggingMatch As Match = TokenLoggingPattern.Match(line)
            If loggingMatch.Success Then
                Dim loggedVariable As String = loggingMatch.Groups("name").Value
                If Not String.Equals(loggedVariable, "BOT_TOKEN", StringComparison.OrdinalIgnoreCase) OrElse
                   line.Contains("discord", StringComparison.OrdinalIgnoreCase) OrElse
                   (contextPath IsNot Nothing AndAlso contextPath.Contains("discord", StringComparison.OrdinalIgnoreCase)) Then
                    Return New DetectionInfo With {
                        .Severity = "Medium",
                        .Category = "Possible token logging statement",
                        .Recommendation = "Do not write tokens to application, debug, or CI logs. Remove the logging statement and assess whether existing logs need restricted access or cleanup."
                    }
                End If
            End If

            Dim assignment As Match = DiscordAssignmentPattern.Match(line)
            If assignment.Success Then
                Dim variableName As String = assignment.Groups("name").Value
                If String.Equals(variableName, "BOT_TOKEN", StringComparison.OrdinalIgnoreCase) AndAlso
                   Not line.Contains("discord", StringComparison.OrdinalIgnoreCase) AndAlso
                   (contextPath Is Nothing OrElse Not contextPath.Contains("discord", StringComparison.OrdinalIgnoreCase)) Then
                    Return Nothing
                End If

                Dim assignedValue As String = assignment.Groups("value").Value.Trim()
                If LooksLikeHardCodedValue(assignedValue) AndAlso Not IsPlaceholderOrReference(assignedValue) Then
                    Return New DetectionInfo With {
                        .Severity = "Medium",
                        .Category = "Possible hard-coded Discord token",
                        .Recommendation = "Verify the value privately. If it is a real token, reset it and load the replacement from an environment variable or secret manager."
                    }
                End If
            End If

            Return Nothing
        End Function

        Private Shared Function LooksLikeHardCodedValue(value As String) As Boolean
            Dim candidate As String = value.Trim().Trim(""""c, "'"c, "`"c, ","c, ";"c, ")"c, "}"c)
            If candidate.Length < 12 Then Return False
            Dim usefulCharacters As Integer = 0
            For Each character As Char In candidate
                If Char.IsLetterOrDigit(character) OrElse character = "-"c OrElse character = "_"c OrElse character = "."c Then
                    usefulCharacters += 1
                End If
            Next
            Return usefulCharacters >= 10
        End Function

        Private Shared Function IsPlaceholderOrReference(value As String) As Boolean
            Dim candidate As String = value.Trim().Trim(""""c, "'"c, "`"c, ","c, ";"c, ")"c, "}"c)
            Dim lowered As String = candidate.ToLowerInvariant()

            If lowered.Length = 0 Then Return True
            If lowered.StartsWith("$", StringComparison.Ordinal) OrElse lowered.StartsWith("%", StringComparison.Ordinal) Then Return True
            Dim referenceMarkers As String() = {
                "${", "%discord", "process.env", "os.environ", "getenv", "environment.getenvironmentvariable",
                "secrets.", "secretmanager", "keyvault", "vault.", "user-secrets", "config.get", "config.", "env."
            }
            For Each marker As String In referenceMarkers
                If lowered.Contains(marker, StringComparison.Ordinal) Then Return True
            Next

            Dim placeholders As String() = {
                "your-token", "your_token", "replace-me", "replace_me", "changeme", "change-me",
                "placeholder", "example", "dummy", "redacted", "xxxxx", "xxxx", "<token>",
                "none", "null", "todo", "insert-token-here"
            }
            For Each marker As String In placeholders
                If lowered.Contains(marker, StringComparison.Ordinal) Then Return True
            Next

            Return False
        End Function

        Private Shared Function IsSensitiveEnvironmentFile(relativePath As String) As Boolean
            Dim name As String = Path.GetFileName(relativePath).ToLowerInvariant()
            If Not (name = ".env" OrElse name.StartsWith(".env.", StringComparison.Ordinal)) Then Return False
            Dim safeNames As String() = {".env.example", ".env.sample", ".env.template", ".env.dist", ".env.test.example"}
            Return Not safeNames.Contains(name, StringComparer.OrdinalIgnoreCase)
        End Function

        Private Shared Sub ScanGitHistory(
            rootPath As String,
            result As ScanResult,
            seen As HashSet(Of String),
            progress As IProgress(Of ScanProgress),
            cancellationToken As Threading.CancellationToken
        )
            Try
                Dim startInfo As New ProcessStartInfo("git") With {
                    .WorkingDirectory = rootPath,
                    .UseShellExecute = False,
                    .RedirectStandardOutput = True,
                    .RedirectStandardError = True,
                    .CreateNoWindow = True
                }
                startInfo.ArgumentList.Add("-c")
                startInfo.ArgumentList.Add("core.fsmonitor=false")
                startInfo.ArgumentList.Add("--no-pager")
                startInfo.ArgumentList.Add("log")
                startInfo.ArgumentList.Add("--all")
                startInfo.ArgumentList.Add("--patch")
                startInfo.ArgumentList.Add("--format=")
                startInfo.ArgumentList.Add("--no-ext-diff")
                startInfo.ArgumentList.Add("--no-textconv")
                startInfo.Environment("GIT_PAGER") = "cat"
                startInfo.Environment("GIT_OPTIONAL_LOCKS") = "0"

                Using process As New Process With {.StartInfo = startInfo}
                    If Not process.Start() Then
                        result.Warnings.Add("Git history scan could not be started.")
                        Return
                    End If

                    Dim errorTask As Task(Of String) = process.StandardError.ReadToEndAsync()
                    Dim currentPath As String = "(path unavailable)"
                    Dim currentLine As Integer = 0
                    Dim insideHunk As Boolean = False
                    Dim historyLines As Integer = 0

                    While Not process.StandardOutput.EndOfStream
                        If cancellationToken.IsCancellationRequested Then
                            Try
                                process.Kill(True)
                            Catch
                            End Try
                            cancellationToken.ThrowIfCancellationRequested()
                        End If

                        Dim gitLine As String = process.StandardOutput.ReadLine()
                        If gitLine Is Nothing Then Exit While

                        If gitLine.StartsWith("diff --git ", StringComparison.Ordinal) Then
                            insideHunk = False
                            currentLine = 0
                        ElseIf Not insideHunk AndAlso gitLine.StartsWith("+++ b/", StringComparison.Ordinal) Then
                            currentPath = NormalizeGitPath(gitLine.Substring(6))
                        ElseIf gitLine.StartsWith("@@ ", StringComparison.Ordinal) Then
                            Dim hunk As Match = HunkPattern.Match(gitLine)
                            If hunk.Success Then
                                Integer.TryParse(hunk.Groups("line").Value, currentLine)
                                insideHunk = True
                            Else
                                insideHunk = False
                            End If
                        ElseIf insideHunk Then
                            If gitLine.StartsWith("+", StringComparison.Ordinal) Then
                                historyLines += 1
                                Dim content As String = gitLine.Substring(1)
                                If content.Length <= MaxLineCharacters Then
                                    Dim detection As DetectionInfo = DetectLine(content, currentPath)
                                    If detection IsNot Nothing Then
                                        AddFinding(result, seen, New ScanFinding With {
                                            .Severity = detection.Severity,
                                            .Category = "Git history: " & detection.Category,
                                            .FilePath = currentPath,
                                            .LineNumber = If(currentLine > 0, CType(currentLine, Integer?), Nothing),
                                            .Source = "Git history",
                                            .Recommendation = "Assume a genuine committed token is compromised. Reset it first; then remove the secret from repository history and coordinate cleanup of any remote copies or forks."
                                        })
                                    End If
                                End If
                                currentLine += 1
                            ElseIf gitLine.StartsWith(" ", StringComparison.Ordinal) Then
                                currentLine += 1
                            ElseIf gitLine.StartsWith("\", StringComparison.Ordinal) Then
                                ' Git's marker for a missing final newline; it does not change line numbering.
                            ElseIf gitLine.StartsWith("-", StringComparison.Ordinal) Then
                                ' Deleted lines are not new exposures in this commit.
                            Else
                                insideHunk = False
                            End If
                        End If

                        If historyLines > 0 AndAlso historyLines Mod 5000 = 0 Then
                            progress?.Report(New ScanProgress With {
                                .FilesScanned = result.FilesScanned,
                                .CurrentPath = "Reviewing Git history (added lines inspected: " & historyLines.ToString() & ")"
                            })
                        End If
                    End While

                    process.WaitForExit()
                    Dim ignoredError As String = errorTask.GetAwaiter().GetResult()
                    If process.ExitCode <> 0 Then
                        result.Warnings.Add("Git history could not be read. The working-tree scan still completed.")
                    End If
                End Using
            Catch ex As OperationCanceledException
                Throw
            Catch
                result.Warnings.Add("Git history scan failed. The working-tree scan still completed.")
            End Try
        End Sub

        Private Shared Function NormalizeGitPath(path As String) As String
            Dim candidate As String = path.Trim()
            If candidate.StartsWith("""", StringComparison.Ordinal) Then
                Return "(quoted Git path)"
            End If
            Return candidate.Replace("\", "/")
        End Function

        Private Shared Function ToRelativePath(rootPath As String, fullPath As String) As String
            Try
                Return Path.GetRelativePath(rootPath, fullPath).Replace("\", "/")
            Catch
                Return Path.GetFileName(fullPath)
            End Try
        End Function

        Private Shared Sub AddFinding(result As ScanResult, seen As HashSet(Of String), finding As ScanFinding)
            Dim linePart As String = If(finding.LineNumber.HasValue, finding.LineNumber.Value.ToString(), "-")
            Dim key As String = finding.Source & "|" & finding.FilePath & "|" & linePart & "|" & finding.Category
            If seen.Add(key) Then result.Findings.Add(finding)
        End Sub

        Private Class DetectionInfo
            Public Property Severity As String = String.Empty
            Public Property Category As String = String.Empty
            Public Property Recommendation As String = String.Empty
        End Class
    End Class
End Namespace
