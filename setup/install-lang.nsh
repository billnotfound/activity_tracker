; ============================================================
; setup/install-lang.nsh — taskmonitor114 installer language strings
; ============================================================

; ---- English ----
LangString LANG_SELECT_TITLE  ${LANG_ENGLISH} "Installer Language"
LangString LANG_SELECT_MSG    ${LANG_ENGLISH} "Please select a language."

LangString PRODUCT_DESC       ${LANG_ENGLISH} "Windows activity tracker"
LangString SEC_MAIN_NAME      ${LANG_ENGLISH} "taskmonitor114"
LangString SEC_STARTUP_NAME   ${LANG_ENGLISH} "Auto-start with Windows"
LangString SEC_STARTUP_DESC   ${LANG_ENGLISH} "Launch taskmonitor114 automatically when you log in."

LangString MSG_PROCESS_FOUND  ${LANG_ENGLISH} "taskmonitor114 is currently running.$\n$\nStop the running instance and continue?"
LangString MSG_KILL_RETRY     ${LANG_ENGLISH} "Unable to stop taskmonitor114. Files may be locked.$\n$\nAbort: cancel installation$\nRetry: try to stop again$\nIgnore: continue anyway (files may not be overwritten)"
LangString MSG_FRESH_INSTALL  ${LANG_ENGLISH} "No existing installation found. Performing fresh install to:$\n$INSTDIR"
LangString MSG_UPGRADE        ${LANG_ENGLISH} "Found existing installation at:$\n$INSTDIR$\n$\nUpgrading..."
LangString MSG_ORPHAN_TIER1   ${LANG_ENGLISH} "Cleaning up removed files (live diff)..."
LangString MSG_ORPHAN_TIER2   ${LANG_ENGLISH} "Cleaning up removed files (fallback list)..."
LangString MSG_DELETING       ${LANG_ENGLISH} "Removing: "
LangString MSG_PROTECTED_SKIP ${LANG_ENGLISH} "Skipping protected file: "
LangString MSG_LICENSE_TITLE  ${LANG_ENGLISH} "License Agreement"
LangString MSG_LICENSE_SUBTITLE ${LANG_ENGLISH} "Please review the license terms before installing."

LangString UNINST_DATA_ASK    ${LANG_ENGLISH} "Do you want to keep your activity data?$\n$\nSelecting NO will permanently delete all tracking history, settings, tags and title rules."
LangString UNINST_CONFIRM_DEL ${LANG_ENGLISH} "Are you sure you want to completely remove taskmonitor114?$\n$\nAll activity data will NOT be kept.$\nThis CANNOT BE UNDONE!"
LangString UNINST_CONFIRM     ${LANG_ENGLISH} "Are you sure you want to completely remove taskmonitor114?$\n$\nYour activity data and settings will be kept."
LangString UNINST_DATA_DEL    ${LANG_ENGLISH} "Deleting user data..."
LangString MSG_DETECT_FAIL    ${LANG_ENGLISH} "Old version detection script failed; proceeding as fresh install."

; ---- SimpChinese ----
LangString LANG_SELECT_TITLE  ${LANG_SIMPCHINESE} "安装程序语言"
LangString LANG_SELECT_MSG    ${LANG_SIMPCHINESE} "请选择语言。"

LangString PRODUCT_DESC       ${LANG_SIMPCHINESE} "Windows 活动追踪器"
LangString SEC_MAIN_NAME      ${LANG_SIMPCHINESE} "taskmonitor114"
LangString SEC_STARTUP_NAME   ${LANG_SIMPCHINESE} "开机自启"
LangString SEC_STARTUP_DESC   ${LANG_SIMPCHINESE} "登录时自动启动 taskmonitor114。"

LangString MSG_PROCESS_FOUND  ${LANG_SIMPCHINESE} "检测到 taskmonitor114 正在运行。$\n$\n是否停止旧版本并继续安装？"
LangString MSG_KILL_RETRY     ${LANG_SIMPCHINESE} "无法停止 taskmonitor114。文件可能被占用。$\n$\n中止：取消安装$\n重试：再次尝试停止$\n忽略：继续安装（部分文件可能无法覆盖）"
LangString MSG_FRESH_INSTALL  ${LANG_SIMPCHINESE} "未检测到已安装版本。全新安装到：$\n$INSTDIR"
LangString MSG_UPGRADE        ${LANG_SIMPCHINESE} "检测到已安装版本：$\n$INSTDIR$\n$\n正在升级..."
LangString MSG_ORPHAN_TIER1   ${LANG_SIMPCHINESE} "正在清理已移除的文件（实时对比）..."
LangString MSG_ORPHAN_TIER2   ${LANG_SIMPCHINESE} "正在清理已移除的文件（预计算清单）..."
LangString MSG_DELETING       ${LANG_SIMPCHINESE} "正在删除: "
LangString MSG_PROTECTED_SKIP ${LANG_SIMPCHINESE} "跳过受保护文件: "
LangString MSG_LICENSE_TITLE  ${LANG_SIMPCHINESE} "许可协议"
LangString MSG_LICENSE_SUBTITLE ${LANG_SIMPCHINESE} "安装前请阅读许可条款。"

LangString UNINST_DATA_ASK    ${LANG_SIMPCHINESE} "是否保留活动数据？$\n$\n选择[否]将永久删除所有追踪历史、设置、标签和标题规则。"
LangString UNINST_CONFIRM_DEL ${LANG_SIMPCHINESE} "确定要完全移除 taskmonitor114 吗？$\n$\n所有活动数据将不被保留。$\n此操作不可恢复！"
LangString UNINST_CONFIRM     ${LANG_SIMPCHINESE} "确定要完全移除 taskmonitor114 吗？$\n$\n活动数据和设置将被保留。"
LangString UNINST_DATA_DEL    ${LANG_SIMPCHINESE} "正在删除用户数据..."
LangString MSG_DETECT_FAIL    ${LANG_SIMPCHINESE} "旧版本检测脚本执行失败，按全新安装继续。"
