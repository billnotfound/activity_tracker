; ============================================================
; setup/MultiUserLang.nsh — taskmonitor114 MultiUser.nsh language strings
; (NSIS 3.12 自带 MultiUser.nsh 不捆绑语言文件，此文件补全所需 LangString)
; ============================================================

; ---- English ----
LangString MULTIUSER_TEXT_INSTALLMODE_TITLE ${LANG_ENGLISH} "Install Mode"
LangString MULTIUSER_TEXT_INSTALLMODE_SUBTITLE ${LANG_ENGLISH} "Choose installation options."
LangString MULTIUSER_INNERTEXT_INSTALLMODE_TOP ${LANG_ENGLISH} "Select the installation mode for $(^Name):"
LangString MULTIUSER_INNERTEXT_INSTALLMODE_ALLUSERS ${LANG_ENGLISH} "Install for all users (requires administrator privileges)"
LangString MULTIUSER_INNERTEXT_INSTALLMODE_CURRENTUSER ${LANG_ENGLISH} "Install for the current user only"

; ---- SimpChinese ----
LangString MULTIUSER_TEXT_INSTALLMODE_TITLE ${LANG_SIMPCHINESE} "安装方式"
LangString MULTIUSER_TEXT_INSTALLMODE_SUBTITLE ${LANG_SIMPCHINESE} "选择安装选项。"
LangString MULTIUSER_INNERTEXT_INSTALLMODE_TOP ${LANG_SIMPCHINESE} "选择 $(^Name) 的安装方式："
LangString MULTIUSER_INNERTEXT_INSTALLMODE_ALLUSERS ${LANG_SIMPCHINESE} "为所有用户安装（需要管理员权限）"
LangString MULTIUSER_INNERTEXT_INSTALLMODE_CURRENTUSER ${LANG_SIMPCHINESE} "仅为当前用户安装"
