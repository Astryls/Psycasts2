/* Psycasts² codex - shared chrome + theming + interactive demos */
(function(){
  var inWiki = /\/wiki\//.test(location.pathname);
  var home = inWiki ? '../index.html' : 'index.html';
  var wikiHome = inWiki ? 'index.html' : 'wiki/index.html';
  var planner = inWiki ? 'calculator.html' : 'wiki/calculator.html';
  var onPlanner = /calculator\.html/.test(location.pathname);
  var demo = inWiki ? '../index.html#lab' : '#lab';
  var STEAM = 'https://steamcommunity.com/sharedfiles/filedetails/?id=3749096772';
  var SIGIL = '<svg viewBox="0 0 100 100" aria-hidden="true"><path fill="currentColor" d="M50 5 L58 42 L95 50 L58 58 L50 95 L42 58 L5 50 L42 42 Z"/><circle cx="50" cy="50" r="6.5" fill="var(--bg)"/></svg>';
  var SUN='<svg viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="2" stroke-linecap="round"><circle cx="12" cy="12" r="4.2"/><path d="M12 2v2.4M12 19.6V22M2 12h2.4M19.6 12H22M4.9 4.9l1.7 1.7M17.4 17.4l1.7 1.7M19.1 4.9l-1.7 1.7M6.6 17.4l-1.7 1.7"/></svg>';
  var MOON='<svg viewBox="0 0 24 24" fill="currentColor"><path d="M21 12.8A8.5 8.5 0 1 1 11.2 3a6.8 6.8 0 0 0 9.8 9.8z"/></svg>';

  var WIKI = [
    ['Start','index.html','Overview'],
    ['Start','calculator.html','Build Planner'],
    ['Progression','skill-levels.html','Skill Levels'],
    ['Progression','synergies.html','Synergies'],
    ['Progression','specializations.html','Specializations'],
    ['Progression','enlightenment.html','Enlightenment & Transcendence'],
    ['Cultivation','meditation.html','Meditation & Awakening'],
    ['Cultivation','card-selection.html','Path Cards & Foci'],
    ['Cultivation','auto-stats.html','Psycaster Stats'],
    ['Cultivation','pilgrimages.html','Pilgrimages'],
    ['The World','apotheosis.html','Apotheosis Constellations'],
    ['The World','enemies.html','Enemy Psycasters'],
    ['Reference','casting.html','Casting & Charges'],
    ['Reference','settings.html','Settings Reference']
  ];

  function curTheme(){return document.documentElement.getAttribute('data-theme')==='dark'?'dark':'light';}
  function setTheme(t){
    document.documentElement.setAttribute('data-theme',t);
    try{localStorage.setItem('ps2-theme',t);}catch(e){}
    var b=document.getElementById('themebtn'); if(b) b.innerHTML=(t==='dark'?SUN:MOON);
  }

  /* top nav */
  var navEl=document.getElementById('site-nav');
  if(navEl){
    navEl.className='top';
    navEl.innerHTML=
      '<a href="'+home+'" class="brand">'+SIGIL+' Psycasts\u00b2</a>'+
      '<div class="links">'+
        '<a href="'+home+'">Home</a>'+
        '<a href="'+wikiHome+'"'+(inWiki&&!onPlanner?' class="active"':'')+'>Wiki</a>'+
        '<a href="'+planner+'"'+(onPlanner?' class="active"':'')+'>Planner</a>'+
        '<a href="'+demo+'" class="cta">Live Demo</a>'+
        '<a href="'+STEAM+'" target="_blank" rel="noopener">Steam</a>'+
        '<button id="themebtn" class="themebtn themewrap" title="Toggle light / dark" aria-label="Toggle theme"></button>'+
      '</div>';
    var tb=document.getElementById('themebtn');
    tb.innerHTML=(curTheme()==='dark'?SUN:MOON);
    tb.onclick=function(){setTheme(curTheme()==='dark'?'light':'dark');};
  }

  /* footer */
  var footEl=document.getElementById('site-footer');
  if(footEl){footEl.className='site';footEl.innerHTML='Psycasts\u00b2 Codex &middot; Built for RimWorld 1.6 &middot; Requires Vanilla Psycasts Expanded &middot; <a href="'+wikiHome+'">Wiki</a> &middot; <a href="'+STEAM+'" target="_blank" rel="noopener">Steam Workshop</a>';}

  /* wiki sidebar + prev/next */
  var side=document.getElementById('wikiside');
  if(side){
    var cur=(location.pathname.split('/').pop()||'index.html');
    var html='',lastGrp='';
    WIKI.forEach(function(p){ if(p[0]!==lastGrp){html+='<div class="grp">'+p[0]+'</div>';lastGrp=p[0];} html+='<a href="'+p[1]+'"'+(p[1]===cur?' class="active"':'')+'>'+p[2]+'</a>'; });
    side.innerHTML=html;
    var pn=document.getElementById('pagenav');
    if(pn){
      var i=WIKI.findIndex(function(p){return p[1]===cur;}),out='';
      if(i>0) out+='<a href="'+WIKI[i-1][1]+'"><div class="k">Previous</div><div class="v">'+WIKI[i-1][2]+'</div></a>';
      if(i>=0&&i<WIKI.length-1) out+='<a class="next" href="'+WIKI[i+1][1]+'"><div class="k">Next</div><div class="v">'+WIKI[i+1][2]+'</div></a>';
      pn.innerHTML=out;
    }
  }

  /* reveal on scroll */
  var io=new IntersectionObserver(function(es){es.forEach(function(e){if(e.isIntersecting){e.target.classList.add('in');io.unobserve(e.target);}});},{threshold:.12});
  document.querySelectorAll('.reveal').forEach(function(el){io.observe(el);});

  var $=function(id){return document.getElementById(id);};
  function tierOf(lv){
    if(lv>=11)return['Transcendent','var(--t4)'];
    if(lv>=10)return['Master','var(--t4)'];
    if(lv>=7)return['Expert','var(--t3)'];
    if(lv>=4)return['Adept','var(--t2)'];
    return['Novice','var(--t1)'];
  }
  // tier-1 requirement curve: (n-1)*per + triangular, per = 3
  function levelReq(n){return (n-1)*3 + ((n-1)*(n-2)/2);}

  /* ---- level meter ---- */
  var mR=$('mRange');
  if(mR){
    function meter(){
      var v=+mR.value,t=tierOf(v),per=1.6;
      $('mTier').textContent=t[0];$('mTier').style.color=t[1];
      $('mVal').textContent=v+' / 15';$('mVal').style.color=t[1];
      $('mGain').textContent='+'+per.toFixed(1)+' Damage';
      $('mTotal').textContent='+'+(v*per).toFixed(1)+' Damage';
      $('mReq').textContent='Psycaster level '+levelReq(v);
    }
    mR.addEventListener('input',meter);meter();
  }

  /* ---- synergy lab: level the mates, watch the target empower ---- */
  var lab=$('labMates');
  if(lab){
    // target Firestorm: own level scales Damage; mates feed Damage / Area / Cooldown
    var T={base:{dmg:24,area:3.5,cd:30}, lv:3, ownDmgPer:1.6};
    var MATES=[
      {id:'fp',nm:'Flame Pulse',ic:'FP',stat:'dmg',per:1.6,unit:' Damage',lv:0,desc:'+1.6 Damage / level'},
      {id:'cn',nm:'Cinder Nova',ic:'CN',stat:'area',per:0.4,unit:' Area',lv:0,desc:'+0.4 Area / level'},
      {id:'eb',nm:'Ember Burst',ic:'EB',stat:'cd',per:-0.9,unit:'s Cooldown',lv:0,desc:'-0.9s Cooldown / level'}
    ];
    var motes=$('labMotes'),feed=$('labFeed');
    function sum(stat){var s=0;MATES.forEach(function(m){if(m.stat===stat)s+=m.per*m.lv;});return s;}
    function paint(){
      var t=tierOf(T.lv);
      $('labLv').textContent='Lv '+T.lv;$('labLv').style.color=t[1];
      var dmg=T.base.dmg+T.lv*T.ownDmgPer+sum('dmg');
      var area=T.base.area+sum('area');
      var cd=Math.max(2,T.base.cd+sum('cd'));
      function ud(stat,base){var x=sum(stat);return x?(' <span class="up">'+(x>0?'+':'')+(stat==='cd'?x.toFixed(1)+'s':(stat==='area'?x.toFixed(1):x.toFixed(1)))+'</span>'):'';}
      $('sDmg').innerHTML=dmg.toFixed(1)+ud('dmg');
      $('sArea').innerHTML=area.toFixed(1)+' tiles'+ud('area');
      $('sCd').innerHTML=cd.toFixed(1)+'s'+ud('cd');
    }
    function mote(txt,col){var m=document.createElement('div');m.className='mote';m.textContent=txt;m.style.color=col;m.style.left=(20+Math.random()*55)+'%';m.style.top='6px';motes.appendChild(m);setTimeout(function(){m.remove();},1300);}
    function log(line,dd){var e=feed.querySelector('.empty');if(e)e.remove();var li=document.createElement('li');li.innerHTML='<span>'+line+'</span><span class="dd">'+dd+'</span>';feed.insertBefore(li,feed.firstChild);while(feed.children.length>7)feed.removeChild(feed.lastChild);}
    function step(m,dir){
      var nl=Math.max(0,Math.min(10,m.lv+dir));if(nl===m.lv)return;m.lv=nl;
      $('lv_'+m.id).textContent=m.lv;
      paint();
      if(dir>0){
        var amt=m.per; var col=(m.stat==='cd')?'var(--good)':'var(--good)';
        var disp=(m.stat==='cd')?(amt.toFixed(1)+'s')+' cooldown':((amt>0?'+':'')+amt.toFixed(1)+m.unit.replace(/s? Cooldown| Damage| Area/,function(s){return s.replace('s Cooldown','').replace(' Damage',' dmg').replace(' Area',' area');}));
        mote((m.stat==='cd'?'\u2212':'+')+Math.abs(amt).toFixed(1),'var(--good)');
        log('<b>'+m.nm+'</b> \u2192 Lv '+m.lv+' empowers Firestorm','+'+(m.stat==='cd'?Math.abs(amt).toFixed(1)+'s':Math.abs(amt).toFixed(1)));
      }
    }
    var wrap=$('labMates');
    MATES.forEach(function(m){
      var c=document.createElement('div');c.className='matecard';
      c.innerHTML='<div class="ico">'+m.ic+'</div><div class="grow"><div class="mn">'+m.nm+'</div><div class="mc">'+m.desc+'</div></div>'+
        '<div class="lvbox"><button data-d="-1">\u2212</button><span class="n" id="lv_'+m.id+'">0</span><button data-d="1">+</button></div>';
      c.querySelectorAll('button').forEach(function(b){b.onclick=function(){step(m,+b.dataset.d);};});
      wrap.appendChild(c);
    });
    var lr=$('labLvRange');
    if(lr){lr.addEventListener('input',function(){T.lv=+lr.value;paint();});}
    $('labReset').onclick=function(){MATES.forEach(function(m){m.lv=0;$('lv_'+m.id).textContent='0';});T.lv=3;if(lr)lr.value=3;paint();feed.innerHTML='<li class="empty">Level a path-mate to empower Firestorm.</li>';};
    paint();
  }

  /* ---- charts: inline SVG, colours via CSS classes so they follow the theme live ---- */
  function bt(m){var a=[];for(var h=0;h<=40;h+=2)a.push([h,Math.min((0.02+0.015*h)*m,0.6)]);return a;}
  function btT(h){var a=[],base=0.02+0.015*h;for(var t=0;t<=10;t++){var m=t>3?1+(t-3)*0.15:1;a.push([t,Math.min(base*m,0.6)]);}return a;}
  function roman(n){return ['','I','II','III','IV','V','VI','VII','VIII','IX','X','XI','XII'][n]||(''+n);}
  var CHARTS={
    levelreq:{w:600,h:320,pad:{l:54,r:20,t:16,b:44},
      x:{min:1,max:15,label:'Skill level',ticks:[1,3,5,7,9,11,13,15]},
      y:{min:0,max:150,label:'Psycaster level required',ticks:[0,30,60,90,120,150]},
      series:[
        {name:'Tier 1 ability (shallow)',pts:[[1,0],[2,3],[3,7],[4,12],[5,18],[6,25],[7,33],[8,42],[9,52],[10,63],[11,75],[12,88],[13,102],[14,117],[15,133]]},
        {name:'Tier 6 ability (deepest)',pts:[[1,15],[2,18],[3,22],[4,27],[5,33],[6,40],[7,48],[8,57],[9,67],[10,78],[11,90],[12,103],[13,117],[14,132],[15,148]]}
      ],marks:[{y:30,label:'VPE base cap (psy 30)'}]},
    levelcap:{w:600,h:300,pad:{l:54,r:20,t:16,b:44},
      x:{min:0,max:10,label:'Enlightenment tier',ticks:[0,2,4,6,8,10]},
      y:{min:0,max:160,label:'Psycaster level cap',ticks:[0,40,80,120,160]},
      series:[{name:'Cap = 30 + tier x 12',pts:[[0,30],[1,42],[2,54],[3,66],[4,78],[5,90],[6,102],[7,114],[8,126],[9,138],[10,150]]}],
      marks:[{y:148,label:'level-15 build needs ~148'}]},
    breakthrough:{w:600,h:300,pad:{l:54,r:20,t:16,b:44},
      x:{min:0,max:40,label:'Consecutive meditation hours',ticks:[0,10,20,30,40]},
      y:{min:0,max:0.65,label:'Breakthrough chance / hour',ticks:[0,0.2,0.4,0.6],fmt:function(v){return Math.round(v*100)+'%';}},
      series:[{name:'Tier 0-3  (x1.0)',pts:bt(1)},{name:'Transcendent IV  (x1.15)',pts:bt(1.15)},{name:'Transcendent X  (x2.05)',pts:bt(2.05)}],
      marks:[{y:0.6,label:'hard cap 60%'}]},
    btier:{w:600,h:300,pad:{l:54,r:20,t:16,b:44},
      x:{min:0,max:10,label:'Enlightenment tier',ticks:[0,2,4,6,8,10]},
      y:{min:0,max:0.65,label:'Breakthrough chance / hour',ticks:[0,0.2,0.4,0.6],fmt:function(v){return Math.round(v*100)+'%';}},
      series:[{name:'After a 25h streak',pts:btT(25)},{name:'After a 10h streak',pts:btT(10)}],
      marks:[{y:0.6,label:'hard cap 60%'}]},
    transcost:{w:600,h:300,pad:{l:62,r:20,t:16,b:44},
      x:{min:4,max:10,label:'Transcendent tier reached',ticks:[4,5,6,7,8,9,10],fmt:roman},
      y:{min:0,max:850,label:'Meditation hours for this tier',ticks:[0,200,400,600,800]},
      series:[{name:'Cost = 48 x 1.6^(tier-4)',pts:[[4,48],[5,77],[6,123],[7,197],[8,315],[9,503],[10,805]]}]},
    medlevel:{w:600,h:320,pad:{l:68,r:20,t:16,b:44},
      x:{min:30,max:150,label:'Psycaster level (cap unlocked)',ticks:[30,60,90,120,150]},
      y:{min:0,max:2200,label:'Cumulative meditation hours',ticks:[0,500,1000,1500,2000]},
      series:[{name:'Meditation to unlock this level cap',pts:[[30,0],[42,36],[54,60],[66,84],[78,132],[90,209],[102,332],[114,529],[126,844],[138,1347],[150,2152]]}],
      marks:[{y:84,label:'Illuminated - guided journey ends (Lv 66)'}]},
    psyxp:{w:600,h:320,pad:{l:64,r:20,t:16,b:44},
      x:{min:1,max:50,label:'Psycaster level',ticks:[1,10,20,30,40,50]},
      y:{min:0,max:10000,label:'XP for this level',ticks:[0,2500,5000,7500,10000]},
      series:[{name:'XP for this level',pts:[[1,100],[5,175],[10,352],[15,708],[20,1423],[25,2291],[30,3689],[35,4708],[40,6008],[45,7668],[50,9787]]}],
      marks:[{y:3689,label:'level 30 - base cap'}]},
    accum:{w:600,h:330,pad:{l:66,r:20,t:16,b:44},
      x:{min:0,max:150,label:'Psycaster level',ticks:[0,30,60,90,120,150]},
      y:{min:0,max:6000,label:'Neural-heat offset gained',ticks:[0,1500,3000,4500,6000]},
      series:[
        {name:'Non-retroactive (what you actually get)',pts:[[0,0],[30,450],[42,657],[54,891],[66,1152],[78,1440],[90,1755],[102,2097],[114,2466],[126,2862],[138,3285],[150,3735]]},
        {name:'Retroactive (if past levels were re-valued)',pts:[[0,0],[30,450],[42,724.5],[54,1053],[66,1435.5],[78,1872],[90,2362.5],[102,2907],[114,3505.5],[126,4158],[138,4864.5],[150,5625]]}
      ]},
    ledger:{type:'mk',w:600,h:210,pad:{l:42,r:18,t:28,b:36},ylabel:'per-level rate',
      bands:[{lv:30,rate:1.0,t:'0'},{lv:12,rate:1.15,t:'I'},{lv:12,rate:1.30,t:'II'},{lv:12,rate:1.45,t:'III'},{lv:12,rate:1.60,t:'IV'},{lv:12,rate:1.75,t:'V'},{lv:12,rate:1.90,t:'VI'},{lv:12,rate:2.05,t:'VII'},{lv:12,rate:2.20,t:'VIII'},{lv:12,rate:2.35,t:'IX'},{lv:12,rate:2.50,t:'X'}]}
  };
  function buildMk(host,sp){
    var W=sp.w,H=sp.h,P=sp.pad,tot=0,maxR=0;
    sp.bands.forEach(function(b){tot+=b.lv;if(b.rate>maxR)maxR=b.rate;});
    var bw=W-P.l-P.r, bh=H-P.t-P.b, base=H-P.b, s='<svg viewBox="0 0 '+W+' '+H+'" preserveAspectRatio="xMidYMid meet" role="img">';
    s+='<text class="bkax2" transform="translate(13,'+((P.t+base)/2).toFixed(0)+') rotate(-90)" text-anchor="middle">'+sp.ylabel+'</text>';
    s+='<line class="bkax" x1="'+P.l+'" y1="'+base+'" x2="'+(W-P.r)+'" y2="'+base+'"/>';
    var x=P.l;
    sp.bands.forEach(function(b,i){
      var w=b.lv/tot*bw, h=b.rate/maxR*bh, top=base-h;
      s+='<rect class="bk'+(i%4)+'" x="'+x.toFixed(1)+'" y="'+top.toFixed(1)+'" width="'+(w-1).toFixed(1)+'" height="'+h.toFixed(1)+'" rx="1.5"/>';
      s+='<text class="bkl" x="'+(x+w/2).toFixed(1)+'" y="'+(base+14)+'" text-anchor="middle">'+b.t+'</text>';
      s+='<text class="bkl" x="'+(x+w/2).toFixed(1)+'" y="'+(top-4).toFixed(1)+'" text-anchor="middle">x'+b.rate.toFixed(2)+'</text>';
      x+=w;
    });
    host.innerHTML=s+'</svg>';
  }
  function buildChart(host){
    var sp=CHARTS[host.dataset.chart]; if(!sp) return;
    if(sp.type==='mk'){buildMk(host,sp);return;}
    var W=sp.w,H=sp.h,P=sp.pad;
    var xs=function(v){return P.l+(v-sp.x.min)/(sp.x.max-sp.x.min)*(W-P.l-P.r);};
    var ys=function(v){return H-P.b-(v-sp.y.min)/(sp.y.max-sp.y.min)*(H-P.t-P.b);};
    var yf=sp.y.fmt||function(v){return v;}, xf=sp.x.fmt||function(v){return v;};
    var s='<svg viewBox="0 0 '+W+' '+H+'" preserveAspectRatio="xMidYMid meet" role="img">';
    sp.y.ticks.forEach(function(t){var y=ys(t);s+='<line class="cg" x1="'+P.l+'" y1="'+y+'" x2="'+(W-P.r)+'" y2="'+y+'"/><text class="ct" x="'+(P.l-7)+'" y="'+(y+3.5)+'" text-anchor="end">'+yf(t)+'</text>';});
    sp.x.ticks.forEach(function(t){var x=xs(t);s+='<text class="ct" x="'+x+'" y="'+(H-P.b+16)+'" text-anchor="middle">'+xf(t)+'</text>';});
    s+='<line class="ca" x1="'+P.l+'" y1="'+P.t+'" x2="'+P.l+'" y2="'+(H-P.b)+'"/><line class="ca" x1="'+P.l+'" y1="'+(H-P.b)+'" x2="'+(W-P.r)+'" y2="'+(H-P.b)+'"/>';
    s+='<text class="cax" x="'+((P.l+W-P.r)/2)+'" y="'+(H-5)+'" text-anchor="middle">'+sp.x.label+'</text>';
    s+='<text class="cax" transform="translate(14,'+((P.t+H-P.b)/2)+') rotate(-90)" text-anchor="middle">'+sp.y.label+'</text>';
    (sp.marks||[]).forEach(function(m){var y=ys(m.y);s+='<line class="cm" x1="'+P.l+'" y1="'+y+'" x2="'+(W-P.r)+'" y2="'+y+'"/><text class="cml" x="'+(W-P.r-2)+'" y="'+(y-4)+'" text-anchor="end">'+m.label+'</text>';});
    sp.series.forEach(function(se,i){var pts=se.pts.map(function(p){return xs(p[0]).toFixed(1)+','+ys(p[1]).toFixed(1);}).join(' ');s+='<polyline class="cs cs'+(i+1)+'" points="'+pts+'"/>';});
    var lx=P.l+10, ly=P.t+12;
    sp.series.forEach(function(se,i){s+='<circle class="cd cd'+(i+1)+'" cx="'+lx+'" cy="'+(ly-4)+'" r="4"/><text class="cl" x="'+(lx+9)+'" y="'+ly+'">'+se.name+'</text>';ly+=18;});
    s+='</svg>';
    host.innerHTML=s;
  }
  document.querySelectorAll('[data-chart]').forEach(buildChart);
})();
