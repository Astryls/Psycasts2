/* Psycasts² codex - shared chrome + theming + interactive demos */
(function(){
  var inWiki = /\/wiki\//.test(location.pathname);
  var home = inWiki ? '../index.html' : 'index.html';
  var wikiHome = inWiki ? 'index.html' : 'wiki/index.html';
  var demo = inWiki ? '../index.html#lab' : '#lab';
  var STEAM = 'https://steamcommunity.com/sharedfiles/filedetails/?id=3749096772';
  var SIGIL = '<svg viewBox="0 0 100 100" aria-hidden="true"><path fill="currentColor" d="M50 5 L58 42 L95 50 L58 58 L50 95 L42 58 L5 50 L42 42 Z"/><circle cx="50" cy="50" r="6.5" fill="var(--bg)"/></svg>';
  var SUN='<svg viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="2" stroke-linecap="round"><circle cx="12" cy="12" r="4.2"/><path d="M12 2v2.4M12 19.6V22M2 12h2.4M19.6 12H22M4.9 4.9l1.7 1.7M17.4 17.4l1.7 1.7M19.1 4.9l-1.7 1.7M6.6 17.4l-1.7 1.7"/></svg>';
  var MOON='<svg viewBox="0 0 24 24" fill="currentColor"><path d="M21 12.8A8.5 8.5 0 1 1 11.2 3a6.8 6.8 0 0 0 9.8 9.8z"/></svg>';

  var WIKI = [
    ['Start','index.html','Overview'],
    ['Progression','skill-levels.html','Skill Levels'],
    ['Progression','synergies.html','Synergies'],
    ['Progression','specializations.html','Specializations'],
    ['Progression','enlightenment.html','Enlightenment & Transcendence'],
    ['Cultivation','meditation.html','Meditation & Awakening'],
    ['Cultivation','auto-stats.html','Psycaster Stats'],
    ['Cultivation','pilgrimages.html','Pilgrimages'],
    ['The World','ascensions.html','Ascension Constellations'],
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
        '<a href="'+wikiHome+'"'+(inWiki?' class="active"':'')+'>Wiki</a>'+
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
})();
