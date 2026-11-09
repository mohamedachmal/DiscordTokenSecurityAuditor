Option Strict On
Option Explicit On
Option Infer On

Imports System
Imports System.Diagnostics
Imports System.Drawing
Imports System.IO
Imports System.Threading
Imports System.Threading.Tasks
Imports System.Windows.Forms
Imports DiscordTokenSecurityAuditor.Core

Namespace DiscordTokenSecurityAuditor.Desktop
    Public Class MainForm
        Inherits Form

        Private ReadOnly rootTextBox As New TextBox()
        Private ReadOnly browseButton As New Button()
        Private ReadOnly scanButton As New Button()
        Private ReadOnly cancelButton As New Button()
        Private ReadOnly exportButton As New Button()
        Private ReadOnly historyCheckBox As New CheckBox()
        Private ReadOnly resultGrid As New DataGridView()
        Private ReadOnly summaryLabel As New Label()
        Private ReadOnly statusLabel As New Label()
        Private scanCancellation As CancellationTokenSource
        Private lastResult As ScanResult

        Public Sub New()
            BuildInterface()
        End Sub

        Private Sub BuildInterface()
            Text = "Discord Token Security Auditor"
            StartPosition = FormStartPosition.CenterScreen
            MinimumSize = New Size(980, 650)
            Size = New Size(1180, 760)
            BackColor = Color.FromArgb(247, 248, 250)
            Font = New Font("Segoe UI", 9.0F, FontStyle.Regular)

            Dim layout As New TableLayoutPanel With {
                .Dock = DockStyle.Fill,
                .ColumnCount = 1,
                .RowCount = 6,
                .Padding = New Padding(18),
                .BackColor = BackColor
            }
            layout.RowStyles.Add(New RowStyle(SizeType.Absolute, 76.0F))
            layout.RowStyles.Add(New RowStyle(SizeType.Absolute, 70.0F))
            layout.RowStyles.Add(New RowStyle(SizeType.Absolute, 48.0F))
            layout.RowStyles.Add(New RowStyle(SizeType.Absolute, 30.0F))
            layout.RowStyles.Add(New RowStyle(SizeType.Percent, 100.0F))
            layout.RowStyles.Add(New RowStyle(SizeType.Absolute, 28.0F))
            Controls.Add(layout)

            Dim header As New Panel With {.Dock = DockStyle.Fill}
            Dim title As New Label With {
                .Text = "Discord Token Security Auditor",
                .Font = New Font("Segoe UI Semibold", 20.0F, FontStyle.Bold),
                .ForeColor = Color.FromArgb(35, 43, 58),
                .AutoSize = True,
                .Location = New Point(0, 0)
            }
            Dim subtitle As New Label With {
                .Text = "Find likely token exposures in files and Git repositories you are authorized to inspect.",
                .ForeColor = Color.FromArgb(95, 105, 120),
                .AutoSize = True,
                .Location = New Point(2, 42)
            }
            header.Controls.Add(title)
            header.Controls.Add(subtitle)
            layout.Controls.Add(header, 0, 0)

            Dim pathLayout As New TableLayoutPanel With {
                .Dock = DockStyle.Fill,
                .ColumnCount = 2,
                .RowCount = 2,
                .BackColor = Color.Transparent
            }
            pathLayout.ColumnStyles.Add(New ColumnStyle(SizeType.Percent, 100.0F))
            pathLayout.ColumnStyles.Add(New ColumnStyle(SizeType.Absolute, 118.0F))
            pathLayout.RowStyles.Add(New RowStyle(SizeType.Absolute, 22.0F))
            pathLayout.RowStyles.Add(New RowStyle(SizeType.Percent, 100.0F))
            Dim pathLabel As New Label With {
                .Text = "Folder or repository to scan",
                .Dock = DockStyle.Fill,
                .ForeColor = Color.FromArgb(55, 65, 80),
                .TextAlign = ContentAlignment.MiddleLeft
            }
            pathLayout.Controls.Add(pathLabel, 0, 0)
            pathLayout.SetColumnSpan(pathLabel, 2)
            rootTextBox.Dock = DockStyle.Fill
            rootTextBox.Font = New Font("Segoe UI", 10.0F)
            rootTextBox.Margin = New Padding(0, 3, 8, 0)
            rootTextBox.PlaceholderText = "Choose a project folder..."
            pathLayout.Controls.Add(rootTextBox, 0, 1)
            browseButton.Text = "Browse..."
            browseButton.Dock = DockStyle.Fill
            browseButton.Margin = New Padding(0, 3, 0, 0)
            pathLayout.Controls.Add(browseButton, 1, 1)
            layout.Controls.Add(pathLayout, 0, 1)

            Dim actions As New FlowLayoutPanel With {
                .Dock = DockStyle.Fill,
                .FlowDirection = FlowDirection.LeftToRight,
                .WrapContents = False,
                .Padding = New Padding(0, 5, 0, 0)
            }
            historyCheckBox.Text = "Also inspect Git history (slower)"
            historyCheckBox.AutoSize = True
            historyCheckBox.Margin = New Padding(0, 7, 18, 0)
            actions.Controls.Add(historyCheckBox)

            StyleButton(scanButton, "Scan folder", Color.FromArgb(73, 90, 210), Color.White)
            scanButton.Width = 118
            scanButton.Margin = New Padding(0, 0, 8, 0)
            actions.Controls.Add(scanButton)

            StyleButton(cancelButton, "Cancel", Color.FromArgb(232, 235, 240), Color.FromArgb(55, 65, 80))
            cancelButton.Width = 82
            cancelButton.Enabled = False
            cancelButton.Margin = New Padding(0, 0, 8, 0)
            actions.Controls.Add(cancelButton)

            StyleButton(exportButton, "Export report", Color.FromArgb(232, 235, 240), Color.FromArgb(55, 65, 80))
            exportButton.Width = 112
            exportButton.Enabled = False
            actions.Controls.Add(exportButton)

            Dim helpLink As New LinkLabel With {
                .Text = "How to reset a compromised bot token",
                .AutoSize = True,
                .LinkColor = Color.FromArgb(73, 90, 210),
                .Margin = New Padding(18, 8, 0, 0)
            }
            actions.Controls.Add(helpLink)
            layout.Controls.Add(actions, 0, 2)

            summaryLabel.Dock = DockStyle.Fill
            summaryLabel.Text = "No scan has been run yet."
            summaryLabel.ForeColor = Color.FromArgb(65, 75, 90)
            summaryLabel.TextAlign = ContentAlignment.MiddleLeft
            layout.Controls.Add(summaryLabel, 0, 3)

            ConfigureGrid()
            layout.Controls.Add(resultGrid, 0, 4)

            statusLabel.Dock = DockStyle.Fill
            statusLabel.Text = "Offline scan · token values are never displayed or included in exported reports."
            statusLabel.ForeColor = Color.FromArgb(105, 115, 128)
            statusLabel.TextAlign = ContentAlignment.MiddleLeft
            layout.Controls.Add(statusLabel, 0, 5)

            AddHandler browseButton.Click, AddressOf BrowseForFolder
            AddHandler scanButton.Click, AddressOf StartScan
            AddHandler cancelButton.Click, AddressOf CancelScan
            AddHandler exportButton.Click, AddressOf ExportReport
            AddHandler helpLink.LinkClicked, AddressOf OpenTokenResetGuide
            AddHandler FormClosed, AddressOf FormIsClosing
        End Sub

        Private Shared Sub StyleButton(button As Button, caption As String, backColor As Color, foreColor As Color)
            button.Text = caption
            button.Height = 32
            button.FlatStyle = FlatStyle.Flat
            button.FlatAppearance.BorderSize = 0
            button.BackColor = backColor
            button.ForeColor = foreColor
            button.Font = New Font("Segoe UI Semibold", 9.0F, FontStyle.Bold)
            button.Cursor = Cursors.Hand
        End Sub

        Private Sub ConfigureGrid()
            resultGrid.Dock = DockStyle.Fill
            resultGrid.ReadOnly = True
            resultGrid.AllowUserToAddRows = False
            resultGrid.AllowUserToDeleteRows = False
            resultGrid.AllowUserToResizeRows = False
            resultGrid.MultiSelect = False
            resultGrid.SelectionMode = DataGridViewSelectionMode.FullRowSelect
            resultGrid.RowHeadersVisible = False
            resultGrid.AutoGenerateColumns = False
            resultGrid.AutoSizeColumnsMode = DataGridViewAutoSizeColumnsMode.Fill
            resultGrid.BackgroundColor = Color.White
            resultGrid.BorderStyle = BorderStyle.FixedSingle
            resultGrid.GridColor = Color.FromArgb(230, 233, 238)
            resultGrid.EnableHeadersVisualStyles = False
            resultGrid.ColumnHeadersDefaultCellStyle.BackColor = Color.FromArgb(239, 242, 247)
            resultGrid.ColumnHeadersDefaultCellStyle.ForeColor = Color.FromArgb(55, 65, 80)
            resultGrid.ColumnHeadersDefaultCellStyle.Font = New Font("Segoe UI Semibold", 9.0F, FontStyle.Bold)
            resultGrid.DefaultCellStyle.SelectionBackColor = Color.FromArgb(230, 235, 252)
            resultGrid.DefaultCellStyle.SelectionForeColor = Color.FromArgb(35, 43, 58)
            resultGrid.DefaultCellStyle.WrapMode = DataGridViewTriState.True
            resultGrid.AutoSizeRowsMode = DataGridViewAutoSizeRowsMode.AllCells

            resultGrid.Columns.Add(New DataGridViewTextBoxColumn With {.Name = "Severity", .HeaderText = "Severity", .FillWeight = 62.0F})
            resultGrid.Columns.Add(New DataGridViewTextBoxColumn With {.Name = "Category", .HeaderText = "Finding", .FillWeight = 120.0F})
            resultGrid.Columns.Add(New DataGridViewTextBoxColumn With {.Name = "FilePath", .HeaderText = "File", .FillWeight = 125.0F})
            resultGrid.Columns.Add(New DataGridViewTextBoxColumn With {.Name = "Line", .HeaderText = "Line", .FillWeight = 42.0F})
            resultGrid.Columns.Add(New DataGridViewTextBoxColumn With {.Name = "Source", .HeaderText = "Source", .FillWeight = 70.0F})
            resultGrid.Columns.Add(New DataGridViewTextBoxColumn With {.Name = "Recommendation", .HeaderText = "Recommended action", .FillWeight = 195.0F})

            AddHandler resultGrid.CellFormatting, AddressOf FormatSeverityCell
        End Sub

        Private Sub BrowseForFolder(sender As Object, e As EventArgs)
            Using dialog As New FolderBrowserDialog With {
                .Description = "Choose a folder or Git repository you own or are authorized to inspect.",
                .UseDescriptionForTitle = True,
                .ShowNewFolderButton = False
            }
                If dialog.ShowDialog(Me) = DialogResult.OK Then
                    rootTextBox.Text = dialog.SelectedPath
                End If
            End Using
        End Sub

        Private Async Sub StartScan(sender As Object, e As EventArgs)
            Dim target As String = rootTextBox.Text.Trim()
            If Not Directory.Exists(target) Then
                MessageBox.Show(Me, "Choose an existing folder that you own or are authorized to inspect.", "Folder required", MessageBoxButtons.OK, MessageBoxIcon.Information)
                Return
            End If

            scanCancellation?.Dispose()
            scanCancellation = New CancellationTokenSource()
            scanButton.Enabled = False
            cancelButton.Enabled = True
            browseButton.Enabled = False
            historyCheckBox.Enabled = False
            exportButton.Enabled = False
            resultGrid.Rows.Clear()
            lastResult = Nothing
            summaryLabel.Text = "Scanning..."
            statusLabel.Text = "Inspecting local files. No token values will be shown."

            Dim progress As New Progress(Of ScanProgress)(
                Sub(update As ScanProgress)
                    statusLabel.Text = "Visited " & update.FilesScanned.ToString() & " files · " & update.CurrentPath
                End Sub)

            Try
                Dim result As ScanResult = Await TokenScanner.ScanAsync(
                    target,
                    historyCheckBox.Checked,
                    progress,
                    scanCancellation.Token)

                lastResult = result
                For Each finding As ScanFinding In result.Findings
                    Dim lineText As String = If(finding.LineNumber.HasValue, finding.LineNumber.Value.ToString(), "—")
                    resultGrid.Rows.Add(finding.Severity, finding.Category, finding.FilePath, lineText, finding.Source, finding.Recommendation)
                Next

                summaryLabel.Text = String.Format(
                    "{0} potential finding(s) · {1} files visited · {2} skipped · {3:N1}s",
                    result.Findings.Count,
                    result.FilesScanned,
                    result.FilesSkipped,
                    result.ElapsedMilliseconds / 1000.0R)

                If result.Warnings.Count > 0 Then
                    statusLabel.Text = String.Join(" ", result.Warnings)
                ElseIf result.Findings.Count = 0 Then
                    statusLabel.Text = "No likely Discord token exposures detected by these heuristics. This is not a guarantee that the repository is secret-free."
                Else
                    statusLabel.Text = "Review each finding. A format match is a lead, not proof that a token is active."
                End If
                exportButton.Enabled = True
            Catch ex As OperationCanceledException
                summaryLabel.Text = "Scan cancelled."
                statusLabel.Text = "No report was exported. Run another scan when ready."
            Catch
                MessageBox.Show(Me, "The scan could not complete. Check that the folder is readable and try again.", "Scan error", MessageBoxButtons.OK, MessageBoxIcon.Error)
                summaryLabel.Text = "Scan failed."
                statusLabel.Text = "Check folder permissions and available disk access."
            Finally
                scanButton.Enabled = True
                cancelButton.Enabled = False
                browseButton.Enabled = True
                historyCheckBox.Enabled = True
                scanCancellation?.Dispose()
                scanCancellation = Nothing
            End Try
        End Sub

        Private Sub CancelScan(sender As Object, e As EventArgs)
            scanCancellation?.Cancel()
            statusLabel.Text = "Cancellation requested..."
        End Sub

        Private Sub ExportReport(sender As Object, e As EventArgs)
            If lastResult Is Nothing Then Return

            Using dialog As New SaveFileDialog With {
                .Title = "Export sanitized scan report",
                .Filter = "JSON report (*.json)|*.json",
                .DefaultExt = "json",
                .AddExtension = True,
                .FileName = "discord-token-audit-report.json"
            }
                If dialog.ShowDialog(Me) <> DialogResult.OK Then Return
                Try
                    ReportWriter.SaveJson(dialog.FileName, lastResult)
                    MessageBox.Show(Me, "The sanitized report was saved. It contains finding metadata only, not token values.", "Report exported", MessageBoxButtons.OK, MessageBoxIcon.Information)
                Catch
                    MessageBox.Show(Me, "The report could not be saved to that location.", "Export error", MessageBoxButtons.OK, MessageBoxIcon.Error)
                End Try
            End Using
        End Sub

        Private Sub FormatSeverityCell(sender As Object, e As DataGridViewCellFormattingEventArgs)
            If e.RowIndex < 0 OrElse resultGrid.Columns(e.ColumnIndex).Name <> "Severity" Then Return
            Dim severity As String = Convert.ToString(e.Value)
            If String.Equals(severity, "High", StringComparison.OrdinalIgnoreCase) Then
                e.CellStyle.ForeColor = Color.FromArgb(170, 45, 45)
                e.CellStyle.Font = New Font("Segoe UI Semibold", 9.0F, FontStyle.Bold)
            ElseIf String.Equals(severity, "Medium", StringComparison.OrdinalIgnoreCase) Then
                e.CellStyle.ForeColor = Color.FromArgb(155, 105, 25)
                e.CellStyle.Font = New Font("Segoe UI Semibold", 9.0F, FontStyle.Bold)
            End If
        End Sub

        Private Sub OpenTokenResetGuide(sender As Object, e As LinkLabelLinkClickedEventArgs)
            Try
                Process.Start(New ProcessStartInfo("https://support-dev.discord.com/hc/en-us/articles/6470840524311-Why-can-t-I-copy-my-bot-s-token") With {
                    .UseShellExecute = True
                })
            Catch
                MessageBox.Show(Me, "Open the Discord Developer Support article: Why can't I copy my bot's token?", "Token reset guide", MessageBoxButtons.OK, MessageBoxIcon.Information)
            End Try
        End Sub

        Private Sub FormIsClosing(sender As Object, e As FormClosedEventArgs)
            scanCancellation?.Cancel()
            scanCancellation?.Dispose()
        End Sub
    End Class
End Namespace
