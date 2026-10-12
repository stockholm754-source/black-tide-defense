#!/bin/bash
# CL-001 跑局窗口：三条路线各一局，带看门狗。
# 为什么需要看门狗：RunWindowTool 跑完自己调 ExitPlaymode → Exit(0)，15:17 那局实测卡在退出流程，
# 进程不消失、Temp/TuanjieLockfile 变孤儿锁，下一个 batchmode 会直接无声退出（DS-002b 记录过同一根因）。
# 用法：bash collab/tools/run_window.sh [标签]
set -u
P='C:/Users/Administrator/Documents/gpt/outputs/black-tide-slice'
ED='/d/deepseek/2022.3.62t15/Editor/Tuanjie.exe'
PSH='/c/Windows/System32/WindowsPowerShell/v1.0/powershell.exe'
TAG=${1:-cl}
mkdir -p "$P/Logs"
rm -f "$P/collab/tools/run_finished.txt"

finish_marker_wait() {           # $1 = 最长秒数
  local n=0
  while [ ! -f "$P/collab/tools/run_finished.txt" ] && [ $n -lt "$1" ]; do sleep 5; n=$((n+5)); done
  [ -f "$P/collab/tools/run_finished.txt" ] && return 0 || return 1
}

cleanup() {                       # 等它自己退 30 秒，不退就强杀并清孤儿锁
  local n=0
  while [ $n -lt 30 ]; do
    left=$("$PSH" -NoProfile -Command '(Get-Process Tuanjie -ErrorAction SilentlyContinue | Measure-Object).Count' 2>/dev/null | tr -d '[:space:]')
    [ "$left" = "0" ] && break
    sleep 5; n=$((n+5))
  done
  "$PSH" -NoProfile -Command 'Stop-Process -Name Tuanjie -Force -ErrorAction SilentlyContinue' >/dev/null 2>&1
  sleep 4
  rm -f "$P/Temp/TuanjieLockfile"
}

for STRAT in 稳健发育 激进净化 贪信使; do
  SHORT=$(echo "$STRAT" | cut -c1-4)
  rm -f "$P/collab/tools/run_finished.txt"
  printf '%s\n8\n' "$STRAT" > "$P/collab/tools/run_strategy.txt"
  LOG="$P/Logs/run-${TAG}-${SHORT}.txt"
  echo "=== $(date +%H:%M:%S) 开局：$STRAT → $LOG"
  "$ED" -batchmode -projectPath 'C:\Users\Administrator\Documents\gpt\outputs\black-tide-slice' \
        -executeMethod BlackTide.EditorTools.RunWindowTool.Run -logFile "$LOG" >/dev/null 2>&1 &
  if finish_marker_wait 900; then
    echo "    结束标记：$(cat "$P/collab/tools/run_finished.txt")"
  else
    echo "    ⚠ 超时 900 秒没看到结束标记（本局按失败记录，不重试）"
    grep -oE '\[RunWindow\] failed[^"]*|error CS[0-9]*' "$LOG" | head -3
  fi
  cleanup
  echo "    清理后进程：$("$PSH" -NoProfile -Command '(Get-Process Tuanjie -ErrorAction SilentlyContinue | Measure-Object).Count' 2>/dev/null | tr -d '[:space:]')  锁：$([ -f "$P/Temp/TuanjieLockfile" ] && echo 有 || echo 无)"
done

rm -f "$P/collab/tools/run_strategy.txt" "$P/collab/tools/run_finished.txt"
echo "=== $(date +%H:%M:%S) 窗口结束，注入文件已清"
ls -t "$P/Captures/runs/"run_*.csv | head -4
