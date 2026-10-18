Unicode true
!include MUI2.nsh
!include nsDialogs.nsh
!include LogicLib.nsh
!include FileFunc.nsh
!include x64.nsh
!include WinVer.nsh

!ifndef VERSION
!error "Provide VERSION, ARCH, ROOT, STAGE and OUTPUT through scripts/package.ps1"
!endif
Name "WinWhisper"
OutFile "${OUTPUT}"
InstallDir "$LOCALAPPDATA\Programs\WinWhisper"
RequestExecutionLevel user
ManifestDPIAware true
ManifestLongPathAware true
SetCompressor /SOLID lzma
SetCompressorDictSize 32
SetDatablockOptimize on
ShowInstDetails show
ShowUninstDetails show
VIProductVersion "0.1.0.0"
VIAddVersionKey "ProductName" "WinWhisper"
VIAddVersionKey "ProductVersion" "${VERSION}"
VIAddVersionKey "FileDescription" "WinWhisper ${ARCH} Setup"
VIAddVersionKey "FileVersion" "0.1.0.0"
VIAddVersionKey "LegalCopyright" "Shawn Rain; third-party components retain their licenses"
!define MUI_ICON "${ROOT}\src\MemoAI.App\Assets\Brand\WinWhisper.ico"
!define MUI_UNICON "${ROOT}\src\MemoAI.App\Assets\Brand\WinWhisper.ico"
!define MUI_FINISHPAGE_RUN "$INSTDIR\WinWhisper.exe"
!define MUI_FINISHPAGE_RUN_NOTCHECKED
!define MUI_PAGE_CUSTOMFUNCTION_PRE SkipResumedWelcome
!insertmacro MUI_PAGE_WELCOME
Page custom ScopePage ScopeLeave
!insertmacro MUI_PAGE_DIRECTORY
!insertmacro MUI_PAGE_COMPONENTS
!insertmacro MUI_PAGE_INSTFILES
!insertmacro MUI_PAGE_FINISH
!insertmacro MUI_UNPAGE_CONFIRM
!insertmacro MUI_UNPAGE_INSTFILES
!insertmacro MUI_LANGUAGE "SimpChinese"
!insertmacro MUI_LANGUAGE "English"

Var InstallScope
Var ScopeForced
Var ScopeUser
Var ScopeAll
Var TestInstall
Var UninstallKey
Var ShortcutName
Var CustomDirectory
Var Parameters

LangString ScopeTitle ${LANG_SIMPCHINESE} "安装范围"
LangString ScopeTitle ${LANG_ENGLISH} "Installation scope"
LangString ScopeDescription ${LANG_SIMPCHINESE} "选择哪些账户可以使用 WinWhisper。"
LangString ScopeDescription ${LANG_ENGLISH} "Choose who can use WinWhisper."
LangString ScopeUserText ${LANG_SIMPCHINESE} "仅为当前用户安装（默认，无需管理员权限）"
LangString ScopeUserText ${LANG_ENGLISH} "Install for me only (default, no administrator rights)"
LangString ScopeAllText ${LANG_SIMPCHINESE} "为所有用户安装（需要管理员权限）"
LangString ScopeAllText ${LANG_ENGLISH} "Install for all users (requires administrator rights)"
LangString ScopeNote ${LANG_SIMPCHINESE} "模型、录音和历史分别保存在使用者的账户内。升级与卸载会保留这些数据。桌面快捷方式默认不创建。"
LangString ScopeNote ${LANG_ENGLISH} "Models, recordings and history belong to each account and are preserved on upgrade and uninstall. Desktop shortcuts are optional and off by default."

Function SelectScope
  ${If} $InstallScope == "AllUsers"
    SetShellVarContext all
    ${If} $CustomDirectory != "1"
      StrCpy $INSTDIR "$PROGRAMFILES64\WinWhisper"
      ReadRegStr $0 HKLM "$UninstallKey" "InstallLocation"
      ${If} $0 != ""
        IfFileExists "$0\install-scope.ini" 0 +2
          StrCpy $INSTDIR $0
      ${EndIf}
    ${EndIf}
  ${Else}
    SetShellVarContext current
    ${If} $CustomDirectory != "1"
      StrCpy $INSTDIR "$LOCALAPPDATA\Programs\WinWhisper"
      ReadRegStr $0 HKCU "$UninstallKey" "InstallLocation"
      ${If} $0 != ""
        IfFileExists "$0\install-scope.ini" 0 +2
          StrCpy $INSTDIR $0
      ${EndIf}
    ${EndIf}
  ${EndIf}
FunctionEnd

Function RequireElevation
  System::Call 'kernel32::GetCurrentProcess() p.r0'
  System::Call 'advapi32::OpenProcessToken(p r0, i 8, *p.r1) i.r2'
  ${If} $2 == 0
    Abort "Unable to read the current process token."
  ${EndIf}
  System::Call 'advapi32::GetTokenInformation(p r1, i 20, *i.r2, i 4, *i.r3) i.r4'
  System::Call 'kernel32::CloseHandle(p r1)'
  ${If} $4 == 0
    Abort "Unable to determine administrator privileges."
  ${EndIf}
  ${If} $2 == 0
    StrCpy $0 "/AllUsers"
    ${If} $TestInstall == "1"
      StrCpy $0 "$0 /TestInstall"
    ${EndIf}
    IfSilent 0 +2
      StrCpy $0 "$0 /S"
    ${If} $CustomDirectory == "1"
      StrCpy $0 "$0 /D=$INSTDIR"
    ${EndIf}
    ClearErrors
    ExecShell "runas" "$EXEPATH" "$0"
    ${If} ${Errors}
      Abort "安装所有用户版本需要管理员授权。/ Administrator authorization is required."
    ${EndIf}
    Quit
  ${EndIf}
FunctionEnd

Function .onInit
  ${IfNot} ${AtLeastBuild} 22000
    MessageBox MB_ICONSTOP "WinWhisper requires Windows 11 (build 22000 or later)."
    Abort
  ${EndIf}
  ${IfNot} ${RunningX64}
    MessageBox MB_ICONSTOP "WinWhisper requires a 64-bit Windows system."
    Abort
  ${EndIf}
!if "${ARCH}" == "arm64"
  ${IfNot} ${IsNativeARM64}
    MessageBox MB_ICONSTOP "This installer requires Windows ARM64. Use the x64 or Universal installer."
    Abort
  ${EndIf}
!endif
  SetRegView 64
  StrCpy $InstallScope "CurrentUser"
  StrCpy $TestInstall "0"
  StrCpy $ScopeForced "0"
  StrCpy $CustomDirectory "0"
  ; NSIS consumes /D= before .onInit and removes it from GetParameters.
  ; Preserve the already-initialized $INSTDIR instead of replacing it.
  ${If} $INSTDIR != "$LOCALAPPDATA\Programs\WinWhisper"
    StrCpy $CustomDirectory "1"
  ${EndIf}
  ${GetParameters} $Parameters
  ClearErrors
  ${GetOptions} $Parameters "/D=" $0
  ${IfNot} ${Errors}
    StrCpy $CustomDirectory "1"
  ${EndIf}
  ClearErrors
  ${GetOptions} $Parameters "/TestInstall" $0
  ${IfNot} ${Errors}
    StrCpy $TestInstall "1"
  ${EndIf}
  ${If} $TestInstall == "1"
  ${AndIf} $CustomDirectory != "1"
    SetErrorLevel 3
    Abort "Test installation requires an explicit isolated directory."
  ${EndIf}
  ${If} $TestInstall == "1"
    IfFileExists "$INSTDIR\.winwhisper-installer-test" +3 0
      SetErrorLevel 3
      Abort "Isolated installer-test ownership marker is missing."
  ${EndIf}
  StrCpy $UninstallKey "Software\Microsoft\Windows\CurrentVersion\Uninstall\WinWhisper"
  StrCpy $ShortcutName "WinWhisper"
  ${If} $TestInstall == "1"
    StrCpy $UninstallKey "$UninstallKey.InstallerTest"
    StrCpy $ShortcutName "WinWhisper Installer Test"
  ${EndIf}
  ClearErrors
  ${GetOptions} $Parameters "/AllUsers" $0
  ${IfNot} ${Errors}
    StrCpy $InstallScope "AllUsers"
    StrCpy $ScopeForced "1"
    Call RequireElevation
  ${EndIf}
  Call SelectScope
FunctionEnd

Function SkipResumedWelcome
  ${If} $ScopeForced == "1"
    Abort
  ${EndIf}
FunctionEnd

Function ScopePage
  ${If} $ScopeForced == "1"
    Abort
  ${EndIf}
  !insertmacro MUI_HEADER_TEXT "$(ScopeTitle)" "$(ScopeDescription)"
  nsDialogs::Create 1018
  Pop $0
  ${If} $0 == error
    Abort
  ${EndIf}
  ${NSD_CreateRadioButton} 0 10u 100% 22u "$(ScopeUserText)"
  Pop $ScopeUser
  ${NSD_CreateRadioButton} 0 42u 100% 22u "$(ScopeAllText)"
  Pop $ScopeAll
  ${NSD_CreateLabel} 0 80u 100% 50u "$(ScopeNote)"
  Pop $0
  ${If} $InstallScope == "AllUsers"
    ${NSD_Check} $ScopeAll
  ${Else}
    ${NSD_Check} $ScopeUser
  ${EndIf}
  nsDialogs::Show
FunctionEnd

Function ScopeLeave
  ${NSD_GetState} $ScopeAll $0
  ${If} $0 == ${BST_CHECKED}
    StrCpy $InstallScope "AllUsers"
    Call RequireElevation
  ${Else}
    StrCpy $InstallScope "CurrentUser"
  ${EndIf}
  Call SelectScope
FunctionEnd

!macro TargetLockCheck Prefix
Function ${Prefix}CheckTargetClosed
  IfFileExists "$INSTDIR\WinWhisper.exe" 0 done
  System::Call 'kernel32::CreateFileW(w "$INSTDIR\WinWhisper.exe", i 0x40000000, i 0, p 0, i 3, i 0, p 0) p.r0'
  ${If} $0 == -1
    MessageBox MB_ICONSTOP "请先关闭安装目录中的 WinWhisper，并确认目录可写。/ Close WinWhisper in the target folder and check write permissions." /SD IDOK
    SetErrorLevel 2
    Abort
  ${EndIf}
  System::Call 'kernel32::CloseHandle(p r0)'
done:
FunctionEnd
!macroend
!insertmacro TargetLockCheck ""
!insertmacro TargetLockCheck "un."

Section "WinWhisper（必需 / required）" MainSection
  SectionIn RO
  Call CheckTargetClosed
  SetOutPath "$INSTDIR"
  ClearErrors
!if "${ARCH}" == "Universal"
  ${If} ${IsNativeARM64}
    File /r "${STAGE}\win-arm64\*"
    WriteINIStr "$INSTDIR\install-scope.ini" "Install" "Architecture" "win-arm64"
  ${Else}
    File /r "${STAGE}\win-x64\*"
    WriteINIStr "$INSTDIR\install-scope.ini" "Install" "Architecture" "win-x64"
  ${EndIf}
!else if "${ARCH}" == "arm64"
  File /r "${STAGE}\win-arm64\*"
  WriteINIStr "$INSTDIR\install-scope.ini" "Install" "Architecture" "win-arm64"
!else
  File /r "${STAGE}\win-x64\*"
  WriteINIStr "$INSTDIR\install-scope.ini" "Install" "Architecture" "win-x64"
!endif
  ${If} ${Errors}
    Abort "Application files could not be installed completely. No uninstall registration was written."
  ${EndIf}
  WriteINIStr "$INSTDIR\install-scope.ini" "Install" "Scope" "$InstallScope"
  WriteINIStr "$INSTDIR\install-scope.ini" "Install" "Test" "$TestInstall"
  WriteUninstaller "$INSTDIR\Uninstall.exe"
  WriteRegStr SHCTX "$UninstallKey" "DisplayName" "$ShortcutName"
  WriteRegStr SHCTX "$UninstallKey" "DisplayVersion" "${VERSION}"
  WriteRegStr SHCTX "$UninstallKey" "Publisher" "Shawn Rain"
  WriteRegStr SHCTX "$UninstallKey" "DisplayIcon" "$INSTDIR\WinWhisper.exe"
  WriteRegStr SHCTX "$UninstallKey" "InstallLocation" "$INSTDIR"
  WriteRegStr SHCTX "$UninstallKey" "URLInfoAbout" "https://github.com/ShawnRn/WinWhisper"
  WriteRegStr SHCTX "$UninstallKey" "UninstallString" '$\"$INSTDIR\Uninstall.exe$\"'
  WriteRegDWORD SHCTX "$UninstallKey" "NoModify" 1
  WriteRegDWORD SHCTX "$UninstallKey" "NoRepair" 1
  CreateShortcut "$SMPROGRAMS\$ShortcutName.lnk" "$INSTDIR\WinWhisper.exe" "" "$INSTDIR\WinWhisper.exe"
SectionEnd

Section /o "创建桌面快捷方式 / Desktop shortcut" DesktopSection
  CreateShortcut "$DESKTOP\$ShortcutName.lnk" "$INSTDIR\WinWhisper.exe" "" "$INSTDIR\WinWhisper.exe"
SectionEnd

Function un.onInit
  SetRegView 64
  ReadINIStr $InstallScope "$INSTDIR\install-scope.ini" "Install" "Scope"
  ReadINIStr $TestInstall "$INSTDIR\install-scope.ini" "Install" "Test"
  StrCpy $UninstallKey "Software\Microsoft\Windows\CurrentVersion\Uninstall\WinWhisper"
  StrCpy $ShortcutName "WinWhisper"
  ${If} $TestInstall == "1"
    StrCpy $UninstallKey "$UninstallKey.InstallerTest"
    StrCpy $ShortcutName "WinWhisper Installer Test"
  ${EndIf}
  ${If} $InstallScope == "AllUsers"
    System::Call 'kernel32::GetCurrentProcess() p.r0'
    System::Call 'advapi32::OpenProcessToken(p r0, i 8, *p.r1) i.r2'
    ${If} $2 == 0
      Abort "Unable to read the current process token."
    ${EndIf}
    System::Call 'advapi32::GetTokenInformation(p r1, i 20, *i.r2, i 4, *i.r3) i.r4'
    System::Call 'kernel32::CloseHandle(p r1)'
    ${If} $4 == 0
      Abort "Unable to determine administrator privileges."
    ${EndIf}
    ${If} $2 == 0
      ExecShell "runas" "$INSTDIR\Uninstall.exe" ""
      Quit
    ${EndIf}
    SetShellVarContext all
  ${ElseIf} $InstallScope == "CurrentUser"
    SetShellVarContext current
  ${Else}
    MessageBox MB_ICONSTOP "Installation scope record is missing. No files were removed."
    Abort
  ${EndIf}
  Call un.CheckTargetClosed
FunctionEnd

Section Uninstall
  ; Delete only the compiled list of package-owned files, never the user's data
  ; folder or arbitrary new files placed alongside the application.
  !include "${STAGE}\uninstall-${ARCH}.nsh"
  Delete "$INSTDIR\install-scope.ini"
  Delete "$INSTDIR\Uninstall.exe"
  Delete "$SMPROGRAMS\$ShortcutName.lnk"
  Delete "$DESKTOP\$ShortcutName.lnk"
  DeleteRegKey SHCTX "$UninstallKey"
  RMDir "$INSTDIR"
SectionEnd
