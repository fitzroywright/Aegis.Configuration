internal static class GlossyCss
{
    internal const string Value = """
/* Glossy command-surface treatment matching the approved Configuration reference. */
html,body{background:radial-gradient(circle at 48% -18%,#123f64 0,#061c30 26%,#00101d 62%,#00070d 100%);background-attachment:fixed}
body:before{content:'';position:fixed;inset:0;pointer-events:none;z-index:9999;background:linear-gradient(115deg,rgba(255,255,255,.028),transparent 22%,transparent 73%,rgba(86,188,255,.025)),repeating-linear-gradient(0deg,rgba(255,255,255,.012) 0,rgba(255,255,255,.012) 1px,transparent 1px,transparent 4px);mix-blend-mode:screen}
.topbar{background:linear-gradient(180deg,#092b46 0,#031b2e 45%,#00111f 100%);box-shadow:inset 0 1px 0 rgba(143,218,255,.45),inset 0 -12px 24px rgba(0,0,0,.34),0 8px 24px rgba(0,0,0,.42)}
.cornerbars i:first-child{background:linear-gradient(180deg,#ffd47b 0,#ffae31 38%,#d36d00 100%);box-shadow:inset 0 2px 2px rgba(255,255,255,.7),inset -3px -5px 10px rgba(112,49,0,.42),0 0 14px rgba(255,174,49,.42)}
.cornerbars i:last-child{background:linear-gradient(180deg,#b99aff 0,#8e61ff 42%,#5430bd 100%);box-shadow:inset 0 2px 2px rgba(255,255,255,.58),inset -3px -5px 10px rgba(34,10,92,.5),0 0 14px rgba(142,97,255,.35)}
.gear{color:#62ccff;text-shadow:0 0 5px #8be0ff,0 0 18px #159fe8,0 0 34px rgba(21,159,232,.55);filter:drop-shadow(0 3px 2px #000)}
.brand strong{text-shadow:0 2px 1px #000,0 0 9px rgba(150,218,255,.22)}
.brand em{color:#ffc25c;text-shadow:0 0 12px rgba(255,173,49,.38)}
.site{background:linear-gradient(180deg,#5655ff 0,#2929bd 45%,#151575 100%);border-left-color:#ffb43d;box-shadow:inset 0 2px 1px rgba(255,255,255,.42),inset 0 -5px 12px rgba(0,0,0,.4),0 3px 10px rgba(0,0,0,.45)}
.user,.clock{background:linear-gradient(180deg,rgba(15,54,82,.82),rgba(0,20,35,.9));box-shadow:inset 0 1px 0 rgba(130,210,255,.2)}
.frame{background:linear-gradient(180deg,rgba(2,32,52,.7),rgba(0,10,18,.8));border-color:#4f9bc8;box-shadow:inset 0 1px 0 rgba(188,232,255,.35),inset 0 0 38px rgba(52,176,255,.12),0 10px 30px rgba(0,0,0,.55),0 0 14px rgba(52,176,255,.13)}
.sidebar{background:linear-gradient(90deg,#00111e 0,#052943 72%,#031a2c 100%);box-shadow:inset -12px 0 24px rgba(0,0,0,.3)}
.sidebar a{border-top:1px solid rgba(116,190,232,.06);text-shadow:0 2px 2px #000}
.sidebar a.active{background:linear-gradient(180deg,#ffd47b 0,#ffb13c 32%,#f28c13 65%,#c96b00 100%);box-shadow:inset 0 2px 1px rgba(255,255,255,.75),inset 0 -5px 12px rgba(110,44,0,.35),0 4px 12px rgba(0,0,0,.42),0 0 14px rgba(255,174,49,.2);text-shadow:0 1px 0 rgba(255,255,255,.35)}
.page-head{background:linear-gradient(90deg,rgba(37,19,78,.36),rgba(4,28,47,.15) 45%,transparent);box-shadow:inset 10px 0 22px rgba(142,97,255,.12)}
.search,.toolbar select,.toolbar button{background:linear-gradient(180deg,#0c3553 0,#031d31 50%,#001421 100%);border-color:#317cab;box-shadow:inset 0 1px 0 rgba(162,224,255,.24),inset 0 -3px 7px rgba(0,0,0,.35),0 2px 5px rgba(0,0,0,.35)}
.toolbar button:hover,.detailactions>*:hover,.links a:hover{filter:brightness(1.22)}
.primary{background:linear-gradient(180deg,#39b8ff 0,#0786e7 42%,#005bb8 100%)!important;border:1px solid #63c9ff!important;box-shadow:inset 0 2px 1px rgba(255,255,255,.5),inset 0 -5px 10px rgba(0,35,95,.45),0 3px 10px rgba(0,99,190,.4)!important;text-shadow:0 1px 2px #00315e}
.metric{background:linear-gradient(145deg,#073f68 0,#02243d 48%,#001727 100%);box-shadow:inset 0 1px 0 rgba(151,220,255,.38),inset 0 -8px 16px rgba(0,0,0,.32),0 4px 10px rgba(0,0,0,.35),0 0 10px rgba(8,127,255,.13)}
.metric span{background:radial-gradient(circle at 35% 25%,#76d5ff 0,#168dff 32%,#0756bb 72%,#032d67 100%);box-shadow:inset 0 2px 2px rgba(255,255,255,.58),inset 0 -5px 8px rgba(0,31,91,.5),0 3px 8px rgba(0,0,0,.5),0 0 12px rgba(31,151,255,.35);text-shadow:0 2px 2px rgba(0,0,0,.55)}
.metric.greenbox{background:linear-gradient(145deg,#075b3d 0,#023522 48%,#001d13 100%)}
.metric.greenbox span{background:radial-gradient(circle at 35% 25%,#91ffd1,#20d789 38%,#087d4d 72%,#03472d)}
.metric.amberbox{background:linear-gradient(145deg,#62440b 0,#342506 48%,#1b1200 100%)}
.metric.amberbox span{background:radial-gradient(circle at 35% 25%,#ffe190,#ffad22 38%,#b86600 72%,#6b3700)}
.tablewrap,.mini,.detail,.panel{background:linear-gradient(145deg,rgba(8,43,68,.96),rgba(0,20,34,.98) 48%,rgba(0,12,22,.99));border-color:#327ca7;box-shadow:inset 0 1px 0 rgba(159,219,255,.24),inset 0 -10px 24px rgba(0,0,0,.22),0 5px 16px rgba(0,0,0,.42)}
th{background:linear-gradient(180deg,#174866 0,#0b2b43 55%,#061e31 100%);box-shadow:inset 0 1px 0 rgba(173,225,255,.25),inset 0 -2px 5px rgba(0,0,0,.3);text-shadow:0 1px 2px #000}
td{background:linear-gradient(180deg,rgba(11,48,73,.32),rgba(0,19,32,.22))}tr:hover td,tr.selected td{background:linear-gradient(180deg,rgba(20,103,161,.62),rgba(5,49,83,.66));box-shadow:inset 0 1px 0 rgba(132,211,255,.2)}
.appicon,.heroicon{background:linear-gradient(145deg,#5ed2ff 0,#168cff 32%,#5544e8 68%,#282093 100%);border:1px solid #6acfff;box-shadow:inset 0 2px 2px rgba(255,255,255,.5),inset 0 -5px 9px rgba(21,12,96,.48),0 3px 8px rgba(0,0,0,.48),0 0 11px rgba(45,157,255,.28);text-shadow:0 2px 2px rgba(0,0,0,.55)}
.pill{box-shadow:inset 0 1px 0 rgba(255,255,255,.32),inset 0 -3px 6px rgba(0,0,0,.35),0 2px 4px rgba(0,0,0,.32);text-shadow:0 1px 2px rgba(0,0,0,.65)}
.pill.good{background:linear-gradient(180deg,#18a66d,#08734a 55%,#034d31);border-color:#32e79a}
.pill.warn{background:linear-gradient(180deg,#8d6509,#5c3d00 55%,#332000);border-color:#e6a821}
.tabs{border-color:#347fac;box-shadow:inset 0 1px 0 rgba(255,255,255,.14)}
.tab{background:linear-gradient(180deg,#124163,#062139 55%,#021522);text-shadow:0 1px 2px #000;box-shadow:inset 0 1px 0 rgba(153,217,255,.16)}
.tab.active{background:linear-gradient(180deg,#39b8ff,#087fff 45%,#0752b0);box-shadow:inset 0 2px 1px rgba(255,255,255,.4),inset 0 -5px 9px rgba(0,34,99,.42)}
.reqrow.head{background:linear-gradient(180deg,#174866,#0a2941 55%,#051c2d)}
.reqrow:not(.head){background:linear-gradient(180deg,rgba(10,45,69,.35),rgba(0,18,30,.25))}
.detailactions>*{background:linear-gradient(180deg,#0d3c60,#03213a 55%,#011522);box-shadow:inset 0 1px 0 rgba(135,208,255,.25),inset 0 -4px 8px rgba(0,0,0,.35),0 2px 6px rgba(0,0,0,.35)}
dialog{background:linear-gradient(145deg,#0b3553,#001827 55%,#000d17);box-shadow:inset 0 1px 0 rgba(181,229,255,.3),0 20px 60px rgba(0,0,0,.7),0 0 22px rgba(22,141,224,.25)}
footer{background:linear-gradient(180deg,#041d30,#00101d);border-image:linear-gradient(90deg,#ffc24f,#ff9e20 24%,#a976ff 48%,#526dff 73%,#ffb53b) 1;box-shadow:inset 0 1px 0 rgba(131,205,244,.16)}
@media(prefers-reduced-motion:no-preference){.gear{animation:glowPulse 4s ease-in-out infinite}.dot.green{animation:statusPulse 2.6s ease-in-out infinite}@keyframes glowPulse{50%{text-shadow:0 0 8px #b4edff,0 0 25px #159fe8,0 0 42px rgba(21,159,232,.72)}}@keyframes statusPulse{50%{box-shadow:0 0 10px #20d789}}}
""";
}
