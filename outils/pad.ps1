# Drives the running pad plugin for tests, through its local pipe (plugin\src\Trinite\Pilote.cs): one command per
# connection, one line of reply. PowerShell 7, no window, nothing to install.
#
#   outils\pad.ps1 capture [2]          -> path of the image of the 9 keys (page 1, or 2)
#   outils\pad.ps1 --reel appui <1-18>  -> simulated press (pad places: 1 Codex, 2 Claude, 3 Antigravity, 4-6 their quotas,
#                                          7 Capture, 8 Photo, 9 Dictation; 10-18: page 2)
#   outils\pad.ps1 etat                 -> summary of the last reading
#   outils\pad.ps1 snapshot             -> dated JSON cache, without titles or any extra UI reading
#   outils\pad.ps1 --reel releve        -> forced reading of Antigravity's quota (its Settings > Models page)
#   outils\pad.ps1 etats-en-direct      -> the "Live states" action's question (set or remove the hooks)
#
# Guard (25/10): a press or a reading acts on the real pad and the real apps: only with --reel, on the user's consent.
# Exit code: 0 reply received, 1 plugin absent (no pipe for more than 1 s), 2 refused without --reel, 3 pipe busy for 10 s.
# NEXUSKEYPAD_PAD_PIPE names another pipe (the tests' fake plugin); the real pad's pipe is nexus-trinite-pad.
$ErrorActionPreference = 'Stop'
$reel = $args -contains '--reel'
$mots = @($args | Where-Object { $_ -ne '--reel' })
if ($mots.Count -gt 0 -and $mots[0] -in 'appui', 'releve' -and -not $reel) {
    [Console]::Error.WriteLine("« $($mots[0]) » acts on the real pad: add --reel, on the user's consent.")
    exit 2
}
$commande = if ($mots.Count) { $mots -join ' ' } else { 'aide' }
$nom = if ($env:NEXUSKEYPAD_PAD_PIPE) { $env:NEXUSKEYPAD_PAD_PIPE } else { 'nexus-trinite-pad' }

# One command at a time. Pipe busy (another client): wait until it is free. Pipe absent: the plugin recreates it after each
# command; "plugin absent" only when it is missing for more than one second in a row. 10 s at most.
$horloge = [Diagnostics.Stopwatch]::StartNew()
$absentDepuis = $null
while ($horloge.ElapsedMilliseconds -lt 10000) {
    if (-not [IO.Directory]::EnumerateFiles('\\.\pipe\', $nom).GetEnumerator().MoveNext()) {
        if ($null -eq $absentDepuis) { $absentDepuis = $horloge.ElapsedMilliseconds }
        if ($horloge.ElapsedMilliseconds - $absentDepuis -gt 1000) {
            Write-Output 'pad plugin absent (Logi service stopped, or plugin not loaded: pad on the Logi logo)'
            exit 1
        }
        Start-Sleep -Milliseconds 50
        continue
    }
    $absentDepuis = $null
    $tube = [IO.Pipes.NamedPipeClientStream]::new('.', $nom, [IO.Pipes.PipeDirection]::InOut)
    try {
        try { $tube.Connect([Math]::Max(1, [Math]::Min(2000, 10000 - $horloge.ElapsedMilliseconds))) }
        catch [TimeoutException] { continue } # still busy, or gone between the check and the connection
        # Connected: the command leaves once, never replayed (a second "appui 9" would stop the dictation it started).
        $utf8 = [Text.UTF8Encoding]::new($false)
        $ecrivain = [IO.StreamWriter]::new($tube, $utf8, 1024, $true)
        $ecrivain.Write($commande + "`n")
        $ecrivain.Flush()
        $lecteur = [IO.StreamReader]::new($tube, $utf8, $false, 1024, $true)
        $reponse = $lecteur.ReadLine()
        Write-Output $(if ($null -eq $reponse) { '' } else { $reponse.Trim() })
        exit 0
    } finally {
        $tube.Dispose()
    }
}
Write-Output 'pad pipe busy for 10 s'
exit 3
