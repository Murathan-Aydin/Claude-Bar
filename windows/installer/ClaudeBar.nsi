; Installateur Windows de Claude Bar (NSIS 3).
; Installation par utilisateur dans %LOCALAPPDATA%\Programs\ClaudeBar : aucun droit administrateur.
; Compilé par windows/build.ps1 (Windows) ou windows/build.sh (macOS / Linux).
;
; Variables passées en ligne de commande : VERSION (x.y.z), PUBLISH_DIR, OUT_DIR.
; Installation silencieuse (déploiement par l'IT) : ClaudeBar-Setup.exe /S

Unicode true
ManifestDPIAware true

!include "MUI2.nsh"
!include "LogicLib.nsh"
!include "FileFunc.nsh"

!ifndef VERSION
  !define VERSION "1.0.0"
!endif
!ifndef PUBLISH_DIR
  !define PUBLISH_DIR "../publish"
!endif
!ifndef OUT_DIR
  !define OUT_DIR "../dist"
!endif

!define APP_NAME "Claude Bar"
!define APP_ID "ClaudeBar"
!define APP_EXE "ClaudeBar.exe"
!define PUBLISHER "Murathan Aydin"
!define ICON_SRC "../src/ClaudeBar/app.ico"
!define UNINST_KEY "Software\Microsoft\Windows\CurrentVersion\Uninstall\${APP_ID}"
!define RUN_KEY "Software\Microsoft\Windows\CurrentVersion\Run"

Name "${APP_NAME}"
OutFile "${OUT_DIR}/ClaudeBar-Setup.exe"
InstallDir "$LOCALAPPDATA\Programs\${APP_ID}"
RequestExecutionLevel user
SetCompressor /SOLID lzma
BrandingText "${APP_NAME} ${VERSION}"

; ── Interface ────────────────────────────────────────────────────────────────
!define MUI_ICON "${ICON_SRC}"
!define MUI_UNICON "${ICON_SRC}"
!define MUI_ABORTWARNING

!define MUI_WELCOMEPAGE_TITLE "Installation de Claude Bar"
!define MUI_WELCOMEPAGE_TEXT "Claude Bar affiche ton utilisation de Claude (session de 5 heures et limite hebdomadaire) dans la zone de notification de Windows, près de l'horloge.$\r$\n$\r$\nAucun droit administrateur n'est nécessaire.$\r$\n$\r$\nClique sur Suivant pour installer."
!insertmacro MUI_PAGE_WELCOME

!insertmacro MUI_PAGE_INSTFILES

!define MUI_FINISHPAGE_TITLE "Claude Bar est installé"
!define MUI_FINISHPAGE_TEXT "Au premier lancement, une fenêtre s'ouvre pour te connecter à claude.ai.$\r$\n$\r$\nL'icône apparaît ensuite près de l'horloge (parfois cachée derrière la flèche « ^ »)."
!define MUI_FINISHPAGE_RUN "$INSTDIR\${APP_EXE}"
!define MUI_FINISHPAGE_RUN_TEXT "Lancer Claude Bar maintenant"
!define MUI_FINISHPAGE_SHOWREADME ""
!define MUI_FINISHPAGE_SHOWREADME_TEXT "Lancer Claude Bar au démarrage de Windows"
!define MUI_FINISHPAGE_SHOWREADME_FUNCTION EnableAutostart
!insertmacro MUI_PAGE_FINISH

!insertmacro MUI_UNPAGE_CONFIRM
!insertmacro MUI_UNPAGE_INSTFILES

!insertmacro MUI_LANGUAGE "French"

VIProductVersion "${VERSION}.0"
VIAddVersionKey /LANG=${LANG_FRENCH} "ProductName" "${APP_NAME}"
VIAddVersionKey /LANG=${LANG_FRENCH} "CompanyName" "${PUBLISHER}"
VIAddVersionKey /LANG=${LANG_FRENCH} "FileDescription" "Installation de ${APP_NAME}"
VIAddVersionKey /LANG=${LANG_FRENCH} "FileVersion" "${VERSION}"
VIAddVersionKey /LANG=${LANG_FRENCH} "ProductVersion" "${VERSION}"
VIAddVersionKey /LANG=${LANG_FRENCH} "LegalCopyright" "Copyright (c) 2026 ${PUBLISHER} - MIT"

; ── Fermeture de l'instance en cours (mise à jour / désinstallation) ─────────
!macro CLOSE_RUNNING_APP
  ${If} ${FileExists} "$INSTDIR\${APP_EXE}"
    ExecWait '"$INSTDIR\${APP_EXE}" --quit'
    Sleep 1500
  ${EndIf}
  ; Filet de sécurité si l'ancienne version ne répond pas.
  nsExec::Exec '"$SYSDIR\taskkill.exe" /F /IM ${APP_EXE}'
  Pop $0
!macroend

Function EnableAutostart
  WriteRegStr HKCU "${RUN_KEY}" "${APP_ID}" '"$INSTDIR\${APP_EXE}" --autostart'
FunctionEnd

; ── Installation ─────────────────────────────────────────────────────────────
Section "Claude Bar" SecMain
  SectionIn RO
  SetOutPath "$INSTDIR"
  !insertmacro CLOSE_RUNNING_APP

  File /r "${PUBLISH_DIR}/*"
  File "/oname=app.ico" "${ICON_SRC}"
  WriteUninstaller "$INSTDIR\Uninstall.exe"

  CreateShortCut "$SMPROGRAMS\${APP_NAME}.lnk" "$INSTDIR\${APP_EXE}" "" "$INSTDIR\app.ico" 0

  ; Entrée dans Paramètres > Applications
  WriteRegStr HKCU "${UNINST_KEY}" "DisplayName" "${APP_NAME}"
  WriteRegStr HKCU "${UNINST_KEY}" "DisplayVersion" "${VERSION}"
  WriteRegStr HKCU "${UNINST_KEY}" "Publisher" "${PUBLISHER}"
  WriteRegStr HKCU "${UNINST_KEY}" "DisplayIcon" "$INSTDIR\app.ico"
  WriteRegStr HKCU "${UNINST_KEY}" "InstallLocation" "$INSTDIR"
  WriteRegStr HKCU "${UNINST_KEY}" "UninstallString" '"$INSTDIR\Uninstall.exe"'
  WriteRegStr HKCU "${UNINST_KEY}" "QuietUninstallString" '"$INSTDIR\Uninstall.exe" /S'
  WriteRegStr HKCU "${UNINST_KEY}" "URLInfoAbout" "https://github.com/Murathan-Aydin/Claude-Bar"
  WriteRegDWORD HKCU "${UNINST_KEY}" "NoModify" 1
  WriteRegDWORD HKCU "${UNINST_KEY}" "NoRepair" 1
  ${GetSize} "$INSTDIR" "/S=0K" $0 $1 $2
  IntFmt $0 "0x%08X" $0
  WriteRegDWORD HKCU "${UNINST_KEY}" "EstimatedSize" "$0"
SectionEnd

Function .onInstSuccess
  ; En mode silencieux, pas de page de fin : on active le démarrage auto et on lance l'app.
  ${If} ${Silent}
    Call EnableAutostart
    Exec '"$INSTDIR\${APP_EXE}" --autostart'
  ${EndIf}
FunctionEnd

; ── Désinstallation ──────────────────────────────────────────────────────────
Section "Uninstall"
  !insertmacro CLOSE_RUNNING_APP

  DeleteRegValue HKCU "${RUN_KEY}" "${APP_ID}"
  Delete "$SMPROGRAMS\${APP_NAME}.lnk"

  ; Sécurité : on ne supprime le dossier entier que s'il s'agit bien du dossier de Claude Bar.
  StrCpy $0 "$INSTDIR" "" -9
  ${If} $0 == "${APP_ID}"
  ${AndIf} ${FileExists} "$INSTDIR\${APP_EXE}"
    RMDir /r "$INSTDIR"
  ${Else}
    Delete "$INSTDIR\${APP_EXE}"
    Delete "$INSTDIR\app.ico"
    Delete "$INSTDIR\Uninstall.exe"
    RMDir "$INSTDIR"
  ${EndIf}

  ; Réglages, journal et profil WebView2 (cookies claude.ai)
  RMDir /r "$LOCALAPPDATA\${APP_ID}"

  DeleteRegKey HKCU "${UNINST_KEY}"
SectionEnd
