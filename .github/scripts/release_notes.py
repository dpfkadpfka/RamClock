# CHANGELOG.md의 "## vX.Y.Z · 제목" 칸을 읽어 깃허브 릴리스 제목·설명으로 쓴다.
#   python3 release_notes.py v1.0.46        → 그 버전 설명을 notes.md로, 제목을 title.txt로
#   python3 release_notes.py --all           → 릴리스가 있는 모든 버전의 제목·설명을 고친다 (gh 필요)
import re, subprocess, sys, json
FOOT = ("\n\n---\n**받는 법** 아래 Assets의 `RamClock.exe`를 받아 실행해요. 이미 쓰고 있다면 저절로 업데이트돼요.\n"
        "설치 방법·사용법은 [README](https://github.com/dpfkadpfka/RamClock#readme), "
        "전체 바뀐 점은 [CHANGELOG](https://github.com/dpfkadpfka/RamClock/blob/main/CHANGELOG.md)에서 볼 수 있어요.")

def sections():
    txt = open("CHANGELOG.md", encoding="utf-8").read()
    out = {}
    for part in re.split(r"\n(?=## v)", txt)[1:]:
        head, _, body = part.partition("\n")
        m = re.match(r"## (v[\d.]+)\s*·\s*(.+)", head.strip())
        if m: out[m.group(1)] = (m.group(2).strip(), body.strip())
    return out

S = sections()
if sys.argv[1] == "--all":
    tags = subprocess.run(["gh", "release", "list", "--limit", "500", "--json", "tagName"], capture_output=True, text=True, check=True).stdout
    for t in json.loads(tags):
        v = t["tagName"]
        if v not in S: continue
        title, body = S[v]
        subprocess.run(["gh", "release", "edit", v, "--title", f"RamClock {v} · {title}", "--notes", body + FOOT], check=True)
        print("edited", v)
else:
    v = sys.argv[1]
    title, body = S.get(v, ("", "RamClock " + v))
    open("title.txt", "w", encoding="utf-8").write(f"RamClock {v}" + (f" · {title}" if title else ""))
    open("notes.md", "w", encoding="utf-8").write(body + FOOT)
