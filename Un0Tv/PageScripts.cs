namespace Un0Tv;

// Scripts the page runs.
//
// There is no app-drawn focus ring any more: the D-pad now uses the page's own
// focus movement, so what the arrows do is whatever the site itself provides.
//
// AutoPlay starts playback on its own once the site lands on a watch page, so
// choosing a movie does not need a second press. It clicks the site's own play
// control if the site is waiting for a tap, and does nothing if a video is
// already playing. The "next episode" banner is skipped so it cannot jump the
// queue.
public static class PageScripts
{
    public const string AutoPlay = """
(function(){
  if(window.__un0tvAutoV===3)return;
  window.__un0tvAutoV=3;
  var done=false,tries=0,timer=0;
  var WANT=['play now','watch now','start streaming','start streaming now','play','watch','continue watching','▶ play now','▶ watch now'];
  function txt(el){return (el.innerText||el.textContent||'').trim().toLowerCase().replace(/\s+/g,' ');}
  function playing(){
    var v=document.querySelector('video');
    if(v&&!v.paused&&!v.ended&&v.readyState>2)return true;
    return false;
  }
  function pick(){
    var list=document.querySelectorAll('button,[role=button],a'),best=null,ba=0,i,el,t,r,a;
    for(i=0;i<list.length;i++){
      el=list[i];
      if(el.closest('.wt-an-banner'))continue;
      t=txt(el);
      if(!t||t.length>28)continue;
      if(WANT.indexOf(t)<0)continue;
      r=el.getBoundingClientRect();
      if(r.width<20||r.height<12)continue;
      if(r.bottom<-40||r.top>innerHeight+40)continue;
      a=r.width*r.height;
      if(a>ba){ba=a;best=el;}
    }
    return best;
  }
  function run(){
    if(done)return;
    if(location.pathname.indexOf('/watch')<0)return;
    if(playing()){done=true;return;}
    var el=pick();
    if(el){
      done=true;
      try{el.focus({preventScroll:true});}catch(e){}
      try{el.click();}catch(e){}
      return;
    }
    tries++;
    if(tries<14){clearTimeout(timer);timer=setTimeout(run,700);}
  }
  function reset(){clearTimeout(timer);tries=0;done=false;setTimeout(run,700);}
  try{
    var p=history.pushState;
    history.pushState=function(){var r=p.apply(this,arguments);reset();return r;};
    var rp=history.replaceState;
    history.replaceState=function(){var r=rp.apply(this,arguments);reset();return r;};
    window.addEventListener('popstate',reset);
  }catch(e){}
  setTimeout(run,900);
})();
""";
}
