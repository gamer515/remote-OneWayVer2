from docx import Document
from docx.oxml.ns import qn
from docx.shared import Pt


PATH = r"C:\remote-OneWayVer2\outputs\제안서_유영선_검토본.docx"


def find_exact(doc, text):
    matches = [p for p in doc.paragraphs if p.text == text]
    if len(matches) != 1:
        raise RuntimeError(f"Expected one paragraph for {text!r}, found {len(matches)}")
    return matches[0]


def set_text(paragraph, text):
    paragraph.text = text
    for run in paragraph.runs:
        run.font.name = "맑은 고딕"
        run.font.size = Pt(10)
        r_fonts = run._element.get_or_add_rPr().get_or_add_rFonts()
        r_fonts.set(qn("w:eastAsia"), "맑은 고딕")
        r_fonts.set(qn("w:ascii"), "맑은 고딕")
        r_fonts.set(qn("w:hAnsi"), "맑은 고딕")


def remove_paragraph(paragraph):
    element = paragraph._element
    element.getparent().remove(element)


doc = Document(PATH)

data_old = [
    "• Terrain 데이터: terrain 식별자, prefab, 길이, 배치 지점과 연결된 Encounter 목록을 저장한다.",
    "• Encounter 데이터: placeId, 등장 인물, Story와 Interaction 경로, 요구 해금, 부여 해금과 실행할 미니게임을 저장한다.",
    "• 내장 LLM 입력 데이터: 원문 이야기, 플레이한 카드, 선택 결과, 인물 관계, 현재 terrain, 아이템과 해금 상태를 저장한다.",
    "• 내장 LLM 출력 데이터: 변경 대상 카드, 카드 순번, 수정 문장, 모델 버전과 검증 결과를 저장한다.",
    "• 미니게임 데이터: 실행 조건, 입력 방식, 난이도, 성공 및 실패 결과, 보상과 후속 이야기 연결을 저장한다.",
]
data_new = [
    "• 콘텐츠 데이터: terrain과 Encounter를 기준으로 인물, 이야기 경로, 해금 조건과 미니게임 연결 정보를 관리한다.",
    "• LLM 데이터: 플레이 기록과 현재 상태를 입력으로 구성하고, 변경 문장과 모델 버전 및 검증 결과를 저장한다.",
    "• 진행 데이터: 선택, 인벤토리, 코인, 관계, 해금과 미니게임 결과를 회차별로 저장한다.",
]

algo_old = [
    "• 콘텐츠 변환 알고리즘: Excel 행을 JSON 객체로 변환하고 식별자와 경로의 참조 무결성을 검사한다.",
    "• terrain 구성 알고리즘: 현재 장과 에피소드에 맞는 prefab을 불러온 뒤 JSON 배치 정보로 NPC와 사건을 등록한다.",
    "• 이야기 입력 구성 알고리즘: 저장된 플레이 기록에서 다음 이야기 변경에 필요한 선택과 사건만 추출하여 내장 LLM 입력으로 묶는다.",
    "• 로컬 이야기 변형 알고리즘: 파인튜닝한 한국어 LLM으로 허용된 카드의 문장을 변경하고, 카드 수, 식별자, 순번, JSON 형식과 금지된 변경 여부를 검사한다.",
    "• 미니게임 연결 알고리즘: Encounter의 행동과 조건에 따라 미니게임을 선택하고 결과를 보상, 해금, 관계 수치와 다음 Story에 반영한다.",
    "• 실패 복구 알고리즘: 모델 실행 실패, 데이터 누락 또는 참조 오류가 발생하면 로그를 남기고 검증된 기본 이야기와 설정으로 계속 진행한다.",
]
algo_new = [
    "• 콘텐츠 구성: Excel 데이터를 JSON으로 변환하고 참조 오류를 검사한 뒤 terrain prefab에 NPC와 사건을 배치한다.",
    "• 이야기 변형: 플레이 기록에서 필요한 정보를 추출해 내장 LLM이 이야기를 변경하고 한국어 문맥과 JSON 형식을 검증한다.",
    "• 결과 연결 및 복구: 미니게임 결과를 보상, 해금과 후속 이야기에 반영하며 오류가 발생하면 검증된 기본 데이터로 복구한다.",
]

data_paragraphs = [find_exact(doc, text) for text in data_old]
algo_paragraphs = [find_exact(doc, text) for text in algo_old]

for paragraph, text in zip(data_paragraphs[:3], data_new):
    set_text(paragraph, text)
for paragraph in data_paragraphs[3:]:
    remove_paragraph(paragraph)

for paragraph, text in zip(algo_paragraphs[:3], algo_new):
    set_text(paragraph, text)
for paragraph in algo_paragraphs[3:]:
    remove_paragraph(paragraph)

doc.save(PATH)
print(PATH)
