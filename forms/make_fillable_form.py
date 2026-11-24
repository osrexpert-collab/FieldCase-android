import pikepdf
from pikepdf import Name, Dictionary, Array, String
SRC="/home/claude/formsrc/src.pdf"; OUT="/home/claude/outputs/שומת-נזקים-למילוי.pdf"
pdf=pikepdf.open(SRC)
page=pdf.pages[0]; H=float(page.mediabox[3])
helv=pdf.make_indirect(Dictionary(Type=Name.Font,Subtype=Name.Type1,BaseFont=Name.Helvetica,Encoding=Name.WinAnsiEncoding))
# Hebrew-capable font for the fields: Liberation Sans (Arial metrics, SIL OFL), embedded in full as Type0/Identity-H
from fontTools.ttLib import TTFont
TTF="/usr/share/fonts/truetype/liberation/LiberationSans-Regular.ttf"
tt=TTFont(TTF); upm=tt["head"].unitsPerEm; order=tt.getGlyphOrder(); hmtx=tt["hmtx"]
widths=[round(hmtx[g][0]*1000/upm) for g in order]
cmap=tt.getBestCmap(); gid={n:i for i,n in enumerate(order)}
uni={}
for cp,gn in sorted(cmap.items()):
    uni.setdefault(gid[gn],cp)
lines=["/CIDInit /ProcSet findresource begin","12 dict begin","begincmap","/CIDSystemInfo << /Registry (Adobe) /Ordering (UCS) /Supplement 0 >> def",
       "/CMapName /Adobe-Identity-UCS def","/CMapType 2 def","1 begincodespacerange","<0000> <FFFF>","endcodespacerange"]
items=[(g,c) for g,c in sorted(uni.items()) if c<=0xFFFF]
for k in range(0,len(items),100):
    ch=items[k:k+100]; lines.append(f"{len(ch)} beginbfchar")
    lines+= [f"<{g:04X}> <{c:04X}>" for g,c in ch]; lines.append("endbfchar")
lines+=["endcmap","CMapName currentdict /CMap defineresource pop","end","end"]
tounicode=pdf.make_stream("\n".join(lines).encode())
ff2=pdf.make_stream(open(TTF,"rb").read()); ff2.Length1=len(open(TTF,"rb").read())
hh=tt["hhea"]; os2=tt["OS/2"]; head=tt["head"]
desc=pdf.make_indirect(Dictionary(Type=Name.FontDescriptor,FontName=Name("/LiberationSans"),Flags=32,
    FontBBox=Array([round(v*1000/upm) for v in (head.xMin,head.yMin,head.xMax,head.yMax)]),ItalicAngle=0,
    Ascent=round(hh.ascent*1000/upm),Descent=round(hh.descent*1000/upm),CapHeight=round(getattr(os2,"sCapHeight",1409)*1000/upm),StemV=80,FontFile2=ff2))
cid=pdf.make_indirect(Dictionary(Type=Name.Font,Subtype=Name.CIDFontType2,BaseFont=Name("/LiberationSans"),
    CIDSystemInfo=Dictionary(Registry=String("Adobe"),Ordering=String("Identity"),Supplement=0),
    FontDescriptor=desc,CIDToGIDMap=Name.Identity,DW=round(hmtx[".notdef"][0]*1000/upm),W=Array([0,Array(widths)])))
heb=pdf.make_indirect(Dictionary(Type=Name.Font,Subtype=Name.Type0,BaseFont=Name("/LiberationSans"),Encoding=Name("/Identity-H"),
    DescendantFonts=Array([cid]),ToUnicode=tounicode))
fields=[]
def field(name,tip,x0,top,x1,bottom,size=10,q=2,multiline=False,inset=1.2):
    rect=[x0+inset,H-bottom+inset,x1-inset,H-top-inset]
    ff=(1<<12) if multiline else 0
    w=Dictionary(Type=Name.Annot,Subtype=Name.Widget,FT=Name.Tx,T=String(name),TU=String(tip),
        Rect=Array(rect),F=4,Ff=ff,Q=q,DA=String(f"/LibS {size} Tf 0 g"),
        MK=Dictionary(),P=page.obj)
    w=pdf.make_indirect(w); fields.append(w)

hdr=[(105.7,119.2),(122.6,136.1),(139.6,153.0),(156.5,169.9),(173.4,186.8)]
right=[("inspection_date","תאריך הבדיקה"),("event_date","תאריך אירוע"),("make_model","תוצר ודגם"),("year","שנת ייצור"),("ownership","בעלויות")]
left=[("plate","מס' רישוי"),("odometer","מד אוץ"),("vin","מס' שילדה"),("inspection_place","מקום הבדיקה"),("report_type","סוג דו\"ח")]
for (t,b),(n1,l1),(n2,l2) in zip(hdr,right,left):
    field(n1,l1,309.2,t,473.5,b)
    field(n2,l2,43.1,t,214.0,b)

rows=[201.1,227.0,253.0,278.9,304.9,330.8,356.8,382.7,408.6,434.6,460.6,486.5,512.4,538.3,563.9]
cols=[("used","משומש",43.8,85.8,1,False),("compatible","תואם",86.3,121.9,1,False),("original","מקורי",122.4,157.6,1,False),
      ("parts","פירוט חלפים",158.0,303.1,2,True),("work","מהות התיקון - עבודות",303.6,556.3,2,True)]
for i in range(14):
    t,b=rows[i],rows[i+1]
    for n,l,x0,x1,q,ml in cols:
        field(f"{n}_{i+1}",f"{l} - שורה {i+1}",x0,t,x1,b,size=9,q=q,multiline=ml)

bot=[(578.3,602.8),(603.2,628.7),(629.2,654.7),(655.2,680.6),(681.1,706.6)]
mid=[("bodywork","עבודות פחחות"),("paint","עבודות צבע"),("electric","עבודות חשמל"),("mechanic","עבודות מכונאות"),("alignment","כיוון מתלים")]
lft=[("bumper_repair","שיפוץ מגן"),("reg_309","תקנה 309"),("ac","עבודות מזגן"),("sensors","פו\"ה חיישנים"),("glass","עבודות זגגות")]
for (t,b),(n1,l1),(n2,l2) in zip(bot,mid,lft):
    field(n1,l1,255.8,t,446.9,b,size=10,multiline=True)
    field(n2,l2,43.3,t,187.0,b,size=10,multiline=True)

annots=page.obj.get("/Annots",Array())
for f in fields: annots.append(f)
page.obj.Annots=annots
pdf.Root.AcroForm=Dictionary(Fields=Array(fields),NeedAppearances=True,DA=String("/LibS 10 Tf 0 g"),
    DR=Dictionary(Font=Dictionary(LibS=heb,Helv=helv)))
pdf.docinfo["/Title"]="שומת נזקים - טופס למילוי"
pdf.save(OUT)
print(len(fields),"fields ->",OUT)
