param(
    [Parameter(Mandatory = $true)][string]$InnoCompiler,
    [Parameter(Mandatory = $true)][string]$AppDirectory,
    [Parameter(Mandatory = $true)][string]$ResultsDirectory
)

Describe 'Perfview installer' -Tag 'Installer' {
    BeforeAll {
        $ErrorActionPreference = 'Stop'
        $repository = Split-Path -Parent $PSScriptRoot
        $instance = [Guid]::NewGuid().ToString('N')
        $appId = "PerfviewTaskbar.Test.$instance"
        $appName = "Perfview Installer Test $instance"
        $testRoot = Join-Path $ResultsDirectory $instance
        $startupKey = 'Software\Microsoft\Windows\CurrentVersion\Run'
        $uninstallKey = "Software\Microsoft\Windows\CurrentVersion\Uninstall\${appId}_is1"
        $startShortcut = Join-Path ([Environment]::GetFolderPath('Programs')) "$appName.lnk"
        $desktopShortcut = Join-Path ([Environment]::GetFolderPath('DesktopDirectory')) "$appName.lnk"
        $shell = New-Object -ComObject WScript.Shell

        function Invoke-TestInstaller([string]$File, [string[]]$ExtraArguments) {
            $log = Join-Path $state.Directory ([Guid]::NewGuid().ToString('N') + '.log')
            $arguments = @('/VERYSILENT', '/SUPPRESSMSGBOXES', '/NORESTART', '/SP-', ('/LOG="{0}"' -f $log)) + $ExtraArguments
            # Wait for the entire process tree, including Inno's uninstall cleanup.
            $process = Start-Process -FilePath $File -ArgumentList $arguments -PassThru -Wait -WindowStyle Hidden
            return $process.ExitCode
        }

        function Install-TestApp([string[]]$Options = @()) {
            Invoke-TestInstaller $setup (@(('/DIR="{0}"' -f $state.InstallDirectory)) + $Options) | Should -Be 0
        }

        function Get-StartupCommand {
            $key = [Microsoft.Win32.Registry]::CurrentUser.OpenSubKey($startupKey)
            try { if ($key) { return $key.GetValue($appId) } }
            finally { if ($key) { $key.Dispose() } }
        }

        function Set-StartupCommand([string]$Command) {
            $key = [Microsoft.Win32.Registry]::CurrentUser.CreateSubKey($startupKey)
            try { $key.SetValue($appId, $Command); $state.StartupTouched = $true }
            finally { $key.Dispose() }
        }

        function Get-TestUninstaller {
            $key = [Microsoft.Win32.Registry]::CurrentUser.OpenSubKey($uninstallKey)
            try { if ($key) { return ([string]$key.GetValue('UninstallString')).Trim('"') } }
            finally { if ($key) { $key.Dispose() } }
        }

        function Uninstall-TestApp {
            $uninstaller = Get-TestUninstaller
            $uninstaller | Should -Not -BeNullOrEmpty
            # Only execute an uninstaller belonging to this test's directory.
            $uninstaller.StartsWith($state.InstallDirectory + '\', [StringComparison]::OrdinalIgnoreCase) | Should -BeTrue
            Invoke-TestInstaller $uninstaller @() | Should -Be 0
        }

        New-Item -ItemType Directory -Path $testRoot -Force | Out-Null
        & $InnoCompiler /Qp "/DAppSourceDir=$AppDirectory" "/DTestInstance=$instance" "/O$testRoot" /FInstallerTest (Join-Path $repository 'installer\Perfview.iss')
        if ($LASTEXITCODE -ne 0) { throw 'Test installer compilation failed.' }
        $setup = Join-Path $testRoot 'InstallerTest.exe'
    }

    BeforeEach {
        # Each case starts fresh and is safe to run on its own or in any order.
        $directory = Join-Path $testRoot ([Guid]::NewGuid().ToString('N'))
        New-Item -ItemType Directory -Path $directory | Out-Null
        $state = @{
            Directory = $directory
            InstallDirectory = Join-Path $directory 'Installed App'
            StartupTouched = $false
        }
        $installedExe = Join-Path $state.InstallDirectory 'Perfview.exe'
        $installedConfig = "$installedExe.config"
        $expectedStartup = '"' + $installedExe + '"'
    }

    AfterEach {
        try {
            if (Get-TestUninstaller) { Uninstall-TestApp }
        }
        finally {
            if ($state.StartupTouched) {
                $key = [Microsoft.Win32.Registry]::CurrentUser.OpenSubKey($startupKey, $true)
                try { if ($key) { $key.DeleteValue($appId, $false) } }
                finally { if ($key) { $key.Dispose() } }
            }
        }
    }

    AfterAll {
        if ($shell) { [void][Runtime.InteropServices.Marshal]::FinalReleaseComObject($shell) }
    }

    It 'blocks installation while the application mutex exists' {
        $mutex = New-Object System.Threading.Mutex($false, "Local\$appId")
        try {
            Invoke-TestInstaller $setup @(('/DIR="{0}"' -f $state.InstallDirectory)) | Should -Not -Be 0
            $installedExe | Should -Not -Exist
            Get-TestUninstaller | Should -BeNullOrEmpty
        }
        finally { $mutex.Dispose() }
    }

    It 'installs intact application files and registers a per-user uninstaller' {
        Install-TestApp
        $expectedFiles = @(Get-ChildItem -LiteralPath $AppDirectory -File | Where-Object { $_.Name -in @('Perfview.exe', 'Perfview.exe.config', 'THIRD-PARTY-NOTICES.txt') -or $_.Extension -eq '.dll' } | ForEach-Object Name)
        $expectedFiles | Should -Contain 'LibreHardwareMonitorLib.dll'
        foreach ($file in $expectedFiles) {
            (Get-FileHash -LiteralPath (Join-Path $state.InstallDirectory $file)).Hash |
                Should -Be (Get-FileHash -LiteralPath (Join-Path $AppDirectory $file)).Hash
        }
        $unexpected = @(Get-ChildItem -LiteralPath $state.InstallDirectory -File | Where-Object Name -NotIn ($expectedFiles + @('unins000.exe', 'unins000.dat')))
        $unexpected | Should -HaveCount 0
        Get-TestUninstaller | Should -Exist
    }

    It 'creates a Start menu shortcut without opting into desktop or startup' {
        Install-TestApp
        $startShortcut | Should -Exist
        $shell.CreateShortcut($startShortcut).TargetPath | Should -Be $installedExe
        $desktopShortcut | Should -Not -Exist
        Get-StartupCommand | Should -BeNullOrEmpty
    }

    It 'creates the optional desktop shortcut' {
        Install-TestApp @('/TASKS=desktopicon')
        $desktopShortcut | Should -Exist
        $shell.CreateShortcut($desktopShortcut).TargetPath | Should -Be $installedExe
    }

    It 'migrates an existing startup opt-in and cleans it up on uninstall' {
        Set-StartupCommand '"C:\Old Portable Location\Perfview.exe"'
        Install-TestApp
        Get-StartupCommand | Should -Be $expectedStartup
        Uninstall-TestApp
        Get-StartupCommand | Should -BeNullOrEmpty
    }

    It 'repairs application files and reuses the uninstaller on reinstall' {
        Install-TestApp
        Set-Content -LiteralPath $installedConfig -Value 'Old configuration to replace.'
        Set-StartupCommand $expectedStartup
        Install-TestApp @('/TASKS=desktopicon')
        (Get-FileHash -LiteralPath $installedConfig).Hash | Should -Be (Get-FileHash -LiteralPath (Join-Path $AppDirectory 'Perfview.exe.config')).Hash
        Get-StartupCommand | Should -Be $expectedStartup
        @(Get-ChildItem -LiteralPath $state.InstallDirectory -Filter 'unins*.exe') | Should -HaveCount 1
        $shell.CreateShortcut($desktopShortcut).TargetPath | Should -Be $installedExe
    }

    It 'blocks uninstall while the application mutex exists' {
        Install-TestApp
        $mutex = New-Object System.Threading.Mutex($false, "Local\$appId")
        try {
            Invoke-TestInstaller (Get-TestUninstaller) @() | Should -Not -Be 0
            $installedExe | Should -Exist
            Get-TestUninstaller | Should -Exist
        }
        finally { $mutex.Dispose() }
    }

    It 'removes application files, registration and shortcuts but preserves user data' {
        Install-TestApp @('/TASKS=desktopicon')
        $retainedFile = Join-Path $state.InstallDirectory 'user-data.txt'
        Set-Content -LiteralPath $retainedFile -Value 'Keep user data.'
        Uninstall-TestApp
        $installedExe | Should -Not -Exist
        $installedConfig | Should -Not -Exist
        Get-TestUninstaller | Should -BeNullOrEmpty
        $startShortcut | Should -Not -Exist
        $desktopShortcut | Should -Not -Exist
        (Get-Content -LiteralPath $retainedFile -Raw).Trim() | Should -Be 'Keep user data.'
    }

    It 'removes startup enabled after installation' {
        Install-TestApp
        Set-StartupCommand $expectedStartup
        Uninstall-TestApp
        Get-StartupCommand | Should -BeNullOrEmpty
    }

    It 'preserves startup redirected to another portable copy' {
        Install-TestApp
        $otherCommand = '"C:\Another Portable Copy\Perfview.exe"'
        Set-StartupCommand $otherCommand
        Uninstall-TestApp
        Get-StartupCommand | Should -Be $otherCommand
    }
}
