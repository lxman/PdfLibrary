# Issue 51 — false-empty UsedCodes population (throwaway probe)

documents scanned      : 4708
  parsed OK            : 4659
  failed to parse      : 49

## CONFIRMED (annotation-AP path, exact)
docs with >=1 AP-only font (fully false-empty) : 62
docs with >=1 AP-extended font (partial)       : 2
total AP-only fonts                            : 1712
total AP-extended fonts                        : 2
total codes invisible to the narrow walk       : 9637

### of those, COMPOSITE (Type0) — the only kind whole-face replacement rewrites
docs with >=1 AP-only COMPOSITE font           : 6
docs with >=1 AP-extended COMPOSITE font       : 0
total AP-only composite fonts                  : 904
total AP-extended composite fonts              : 0

### ISSUE-44 FILTER RISK — falsely-undrawn composite SHARING a holder with a drawn font
docs at risk                                   : 0
fonts at risk                                  : 0

## UPPER BOUND (other three paths, structural presence only)
docs containing a Type3 font                   : 84
docs containing a tiling pattern with /Font    : 0
docs containing an ExtGState with /Font        : 0

## Context
docs where some referenced font has no narrow codes : 2677
  (includes genuinely-undrawn fonts — the issue 44 population — so this is NOT the defect count)

## Affected documents (AP path)
cc-main-2021-31-sample/0000_0000849.pdf  fonts=2141 apOnly=893 (composite 893) apExtended=0 (composite 0) extraCodes=2565
cc-main-2021-31-sample/2000_2000488.pdf  fonts=624 apOnly=616 (composite 0) apExtended=0 (composite 0) extraCodes=3696
cc-main-2021-31-sample/2000_2000074.pdf  fonts=394 apOnly=112 (composite 0) apExtended=0 (composite 0) extraCodes=1408
local-708/Dynamic.pdf  fonts=6 apOnly=5 (composite 5) apExtended=0 (composite 0) extraCodes=90
cc-main-2021-31-sample/0000_0000854.pdf  fonts=6 apOnly=4 (composite 0) apExtended=0 (composite 0) extraCodes=136
cc-main-2021-31-sample/6000_6000051.pdf  fonts=4 apOnly=4 (composite 0) apExtended=0 (composite 0) extraCodes=62
cc-main-2021-31-sample/2000_2000915.pdf  fonts=8 apOnly=4 (composite 0) apExtended=0 (composite 0) extraCodes=4
local-708/2025_PIV-Card, Badge, Credential, or Access Control Application.pdf  fonts=25 apOnly=3 (composite 0) apExtended=0 (composite 0) extraCodes=78
cc-main-2021-31-sample/0000_0000425.pdf  fonts=28 apOnly=3 (composite 2) apExtended=0 (composite 0) extraCodes=34
cc-main-2021-31-sample/0000_0000581.pdf  fonts=5 apOnly=2 (composite 0) apExtended=0 (composite 0) extraCodes=114
local-708/P24-1689 50 Pine Bough Road Murphy, NC_ToSign.pdf  fonts=47 apOnly=2 (composite 0) apExtended=0 (composite 0) extraCodes=84
local-708/P24-1689 50 Pine Bough Road Murphy, NC.pdf  fonts=47 apOnly=2 (composite 0) apExtended=0 (composite 0) extraCodes=84
cc-main-2021-31-sample/6000_6000704.pdf  fonts=5 apOnly=2 (composite 0) apExtended=0 (composite 0) extraCodes=68
local-708/template1__1.pdf  fonts=4 apOnly=2 (composite 0) apExtended=0 (composite 0) extraCodes=39
local-708/fw9.pdf  fonts=8 apOnly=2 (composite 0) apExtended=0 (composite 0) extraCodes=32
cc-main-2021-31-sample/6000_6000059.pdf  fonts=11 apOnly=2 (composite 1) apExtended=0 (composite 0) extraCodes=29
local-708/Direct Deposit Authorization Form Filled.pdf  fonts=9 apOnly=2 (composite 0) apExtended=0 (composite 0) extraCodes=27
cc-main-2021-31-sample/6000_6000933.pdf  fonts=6 apOnly=2 (composite 0) apExtended=0 (composite 0) extraCodes=20
cc-main-2021-31-sample/2000_2000213.pdf  fonts=12 apOnly=2 (composite 0) apExtended=1 (composite 0) extraCodes=19
cc-main-2021-31-sample/4000_4000332.pdf  fonts=4 apOnly=2 (composite 0) apExtended=0 (composite 0) extraCodes=15
cc-main-2021-31-sample/0000_0000551.pdf  fonts=72 apOnly=2 (composite 0) apExtended=0 (composite 0) extraCodes=15
cc-main-2021-31-sample/2000_2000777.pdf  fonts=11 apOnly=2 (composite 0) apExtended=0 (composite 0) extraCodes=14
cc-main-2021-31-sample/6000_6000887.pdf  fonts=16 apOnly=2 (composite 2) apExtended=0 (composite 0) extraCodes=8
cc-main-2021-31-sample/6000_6000510.pdf  fonts=76 apOnly=2 (composite 0) apExtended=0 (composite 0) extraCodes=2
local-708/FORM 78-[Jordan].pdf  fonts=7 apOnly=1 (composite 0) apExtended=0 (composite 0) extraCodes=62
cc-main-2021-31-sample/2000_2000901.pdf  fonts=15 apOnly=1 (composite 1) apExtended=0 (composite 0) extraCodes=53
cc-main-2021-31-sample/4000_4000084.pdf  fonts=4 apOnly=1 (composite 0) apExtended=0 (composite 0) extraCodes=52
local-708/Blank Electronic DHS_Form_11000-21 Nov.pdf  fonts=3 apOnly=1 (composite 0) apExtended=0 (composite 0) extraCodes=52
cc-main-2021-31-sample/2000_2000686.pdf  fonts=10 apOnly=1 (composite 0) apExtended=0 (composite 0) extraCodes=51
local-708/CACI CBP Form 78 - BIRD 02 2025.pdf  fonts=7 apOnly=1 (composite 0) apExtended=0 (composite 0) extraCodes=50
local-708/PAS-WAS NC Final Template_Signed.pdf  fonts=8 apOnly=1 (composite 0) apExtended=0 (composite 0) extraCodes=50
local-708/ncui506e March 24 to April 14.pdf  fonts=44 apOnly=1 (composite 0) apExtended=0 (composite 0) extraCodes=49
cc-main-2021-31-sample/6000_6000825.pdf  fonts=4 apOnly=1 (composite 0) apExtended=0 (composite 0) extraCodes=47
cc-main-2021-31-sample/0000_0000702.pdf  fonts=8 apOnly=1 (composite 0) apExtended=0 (composite 0) extraCodes=47
local-708/PAS-WAS NC Final Template.pdf  fonts=8 apOnly=1 (composite 0) apExtended=0 (composite 0) extraCodes=46
cc-main-2021-31-sample/6000_6000908.pdf  fonts=7 apOnly=1 (composite 0) apExtended=0 (composite 0) extraCodes=44
local-708/i-9 - 8-1-2024 Edition-1st page Filled.pdf  fonts=23 apOnly=1 (composite 0) apExtended=0 (composite 0) extraCodes=41
cc-main-2021-31-sample/6000_6000728.pdf  fonts=17 apOnly=1 (composite 0) apExtended=0 (composite 0) extraCodes=39
cc-main-2021-31-sample/0000_0000555.pdf  fonts=7 apOnly=1 (composite 0) apExtended=0 (composite 0) extraCodes=39
cc-main-2021-31-sample/6000_6000561.pdf  fonts=14 apOnly=1 (composite 0) apExtended=0 (composite 0) extraCodes=38
cc-main-2021-31-sample/0000_0000020.pdf  fonts=1 apOnly=1 (composite 0) apExtended=0 (composite 0) extraCodes=36
cc-main-2021-31-sample/6000_6000677.pdf  fonts=9 apOnly=1 (composite 0) apExtended=0 (composite 0) extraCodes=32
cc-main-2021-31-sample/0000_0000277.pdf  fonts=99 apOnly=1 (composite 0) apExtended=0 (composite 0) extraCodes=30
cc-main-2021-31-sample/6000_6000606.pdf  fonts=11 apOnly=1 (composite 0) apExtended=0 (composite 0) extraCodes=28
cc-main-2021-31-sample/2000_2000382.pdf  fonts=13 apOnly=1 (composite 0) apExtended=0 (composite 0) extraCodes=24
cc-main-2021-31-sample/2000_2000428.pdf  fonts=3 apOnly=1 (composite 0) apExtended=0 (composite 0) extraCodes=20
cc-main-2021-31-sample/4000_4000188.pdf  fonts=19 apOnly=1 (composite 0) apExtended=0 (composite 0) extraCodes=16
cc-main-2021-31-sample/0000_0000329.pdf  fonts=6 apOnly=1 (composite 0) apExtended=0 (composite 0) extraCodes=14
cc-main-2021-31-sample/6000_6000839.pdf  fonts=3 apOnly=1 (composite 0) apExtended=1 (composite 0) extraCodes=11
cc-main-2021-31-sample/0000_0000505.pdf  fonts=6 apOnly=1 (composite 0) apExtended=0 (composite 0) extraCodes=8
cc-main-2021-31-sample/6000_6000333.pdf  fonts=9 apOnly=1 (composite 0) apExtended=0 (composite 0) extraCodes=3
cc-main-2021-31-sample/6000_6000958.pdf  fonts=26 apOnly=1 (composite 0) apExtended=0 (composite 0) extraCodes=2
cc-main-2021-31-sample/4000_4000564.pdf  fonts=13 apOnly=1 (composite 0) apExtended=0 (composite 0) extraCodes=1
cc-main-2021-31-sample/4000_4000313.pdf  fonts=20 apOnly=1 (composite 0) apExtended=0 (composite 0) extraCodes=1
cc-main-2021-31-sample/4000_4000259.pdf  fonts=14 apOnly=1 (composite 0) apExtended=0 (composite 0) extraCodes=1
cc-main-2021-31-sample/4000_4000222.pdf  fonts=81 apOnly=1 (composite 0) apExtended=0 (composite 0) extraCodes=1
cc-main-2021-31-sample/4000_4000875.pdf  fonts=6 apOnly=1 (composite 0) apExtended=0 (composite 0) extraCodes=1
local-708/Direct Deposit Authorization Form.pdf  fonts=8 apOnly=1 (composite 0) apExtended=0 (composite 0) extraCodes=1
local-708/i-9 - 8-1-2024 Edition-1st page.pdf  fonts=23 apOnly=1 (composite 0) apExtended=0 (composite 0) extraCodes=1
cc-main-2021-31-sample/2000_2000640.pdf  fonts=9 apOnly=1 (composite 0) apExtended=0 (composite 0) extraCodes=1
cc-main-2021-31-sample/2000_2000176.pdf  fonts=15 apOnly=1 (composite 0) apExtended=0 (composite 0) extraCodes=1
local-708/ncui506e.pdf  fonts=43 apOnly=1 (composite 0) apExtended=0 (composite 0) extraCodes=1

## Parse failures by type
   28  PdfParseException
   12  PdfSecurityException
    9  ArgumentException
