$RepoRoot = $PSScriptRoot
$SourceUrl = "https://github.com/TeamMajdata/MajdataPlay.git"
$SourceBranch = "dev"
$Prefix = "Assets/Plugins/MajdataPlay/FFmpeg"

# 缓存上游仓库，放在 .git 内，不会进入项目提交
$Cache = Join-Path $RepoRoot ".git/majdataplay-ffmpeg-upstream"
$SplitBranch = "ffmpeg-split"

function Invoke-Git {
    param(
        [string]$Directory,
        [string[]]$Arguments
    )

    & git -C $Directory @Arguments
    if ($LASTEXITCODE -ne 0) {
        throw "Git command failed: git $($Arguments -join ' ')"
    }
}

# 检查工作区
$status = git -C $RepoRoot status --porcelain
if ($LASTEXITCODE -ne 0) {
    throw "当前目录不是有效的 Git 仓库。"
}
if ($status) {
    throw "请先提交或暂存当前工作区的修改，再执行同步。"
}

# 目标目录不存在视为首次导入，已存在视为更新
$TargetPath = Join-Path $RepoRoot $Prefix
$IsFirstSync = !(Test-Path $TargetPath)

# 首次创建上游缓存
if (!(Test-Path (Join-Path $Cache ".git"))) {
    if (Test-Path $Cache) {
        throw "缓存目录存在，但不是有效的 Git 仓库：$Cache"
    }

    Write-Host "Cloning upstream repository..."
    & git clone --single-branch --branch $SourceBranch $SourceUrl $Cache
    if ($LASTEXITCODE -ne 0) {
        throw "Clone upstream failed."
    }
}

# 更新上游
Write-Host "Fetching upstream..."
Invoke-Git $Cache @("fetch", "origin", $SourceBranch)

# 重新生成只包含 FFmpeg 目录的提交历史
$existingBranch = git -C $Cache branch --list $SplitBranch
if ($existingBranch) {
    Invoke-Git $Cache @("branch", "-D", $SplitBranch)
}

Write-Host "Splitting FFmpeg directory..."
Invoke-Git $Cache @(
    "subtree", "split",
    "--prefix=$Prefix",
    "origin/$SourceBranch",
    "-b", $SplitBranch
)

# 导入或更新 Subtree
if ($IsFirstSync) {
    Write-Host "Adding FFmpeg subtree..."
    Invoke-Git $RepoRoot @(
        "subtree", "add",
        "--prefix=$Prefix",
        $Cache,
        $SplitBranch,
        "--squash"
    )
}
else {
    Write-Host "Updating FFmpeg subtree..."
    Invoke-Git $RepoRoot @(
        "subtree", "pull",
        "--prefix=$Prefix",
        $Cache,
        $SplitBranch,
        "--squash"
    )
}

Write-Host "FFmpeg sync completed."
