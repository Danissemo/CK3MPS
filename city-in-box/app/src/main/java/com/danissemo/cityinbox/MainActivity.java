package com.danissemo.cityinbox;

import android.app.Activity;
import android.content.Context;
import android.content.SharedPreferences;
import android.graphics.Canvas;
import android.graphics.Color;
import android.graphics.LinearGradient;
import android.graphics.Paint;
import android.graphics.RectF;
import android.graphics.Shader;
import android.os.Bundle;
import android.os.Handler;
import android.view.MotionEvent;
import android.view.View;
import android.view.Window;
import android.view.WindowManager;
import java.util.ArrayList;
import java.util.List;

public final class MainActivity extends Activity {
  @Override public void onCreate(Bundle b) {
    super.onCreate(b);
    requestWindowFeature(Window.FEATURE_NO_TITLE);
    getWindow().setFlags(WindowManager.LayoutParams.FLAG_FULLSCREEN, WindowManager.LayoutParams.FLAG_FULLSCREEN);
    setContentView(new Game(this));
  }

  static final class Game extends View {
    static final int TITLE=0,RIVER=1,BOX=2,CITY=3,HUB=4,HOUSE=5,ALBUM=6,SQUARE=7,SHOP=8,
      BUTCHER=9,RESCUE=10,FAIL=11,GHOSTS=12,COPY=13,COPY_HUB=14,ARCHIVE=15,ACCUSE=16,
      GOOD=17,BAD=18,SECRET=19;
    final Paint p=new Paint(3), line=new Paint(3);
    final List<Hit> hits=new ArrayList<>();
    final Handler h=new Handler();
    final SharedPreferences save;
    int scene=TITLE,loop=1,time=-1; boolean ticking,photo,shore,journal,whisper,archive,red;
    long frame;
    final Runnable tick=new Runnable(){public void run(){frame++;if(ticking&&time>0&&--time==0){scene=BAD;ticking=false;ending("bad");}invalidate();h.postDelayed(this,1000);}};

    Game(Context c){super(c);save=c.getSharedPreferences("city",0);line.setStyle(Paint.Style.STROKE);line.setStrokeWidth(dp(1));h.post(tick);}
    @Override protected void onDetachedFromWindow(){h.removeCallbacks(tick);super.onDetachedFromWindow();}

    @Override protected void onDraw(Canvas c){
      hits.clear(); background(c); header(c);
      switch(scene){
        case TITLE: title(c); break;
        case RIVER: page(c,"НАЧАЛО ИГРЫ","Вас было четверо, но у чёрной реки остались только двое. На берегу лежат деревянные дощечки. За туманом видна противоположная сторона.","Переплыть на дощечках","Подождать остальных"); break;
        case BOX: page(c,"КОРОБКА С ДВЕРЬЮ","На другом берегу стоит огромная коробка с настоящей дверью. Двое других игроков не появляются. Внутри что-то стучит.","Открыть дверь","Осмотреть берег"); break;
        case CITY: page(c,"ГОРОД ВНУТРИ","Коробка раскрывается как подарок. Внутри вырастает город. Правило: найдите убийцу девушки. Не позволяйте жителям касаться вас.","Войти в город"); break;
        case HUB: page(c,"ПЕРВОЕ РАССЛЕДОВАНИЕ",clues()+"\nКуда идти? Лица жителей слишком неподвижны.","Дом девушки","Центральная площадь","Мастерская мясника","Завершить дело"); break;
        case HOUSE: page(c,"ДОМ ДЕВУШКИ","Комната покрыта пылью. В шкафу спрятан альбом. На стене — следы складных створок коробки.","Открыть альбом","Вернуться"); break;
        case ALBUM: page(c,"ФОТОГРАФИЯ","Девушка стоит рядом с молодым человеком. Позади него — эта же река, а в руках лента от коробки. Его лицо зачёркнуто.","Забрать фотографию","Оставить"); break;
        case SQUARE: page(c,"КАСАНИЕ","Слепая жительница хватает вас за рукав. Запускается таймер. Она шепчет: «Фотограф пришёл после смерти. Ищите того, кто открывает город».","Вырваться"); break;
        case SHOP: page(c,"МАСТЕРСКАЯ","Тесак чист. В журнале написано: «Повреждённых игроков восстановить. После спасения оставить 45 секунд». Мясник — часть перезапуска.","Забрать страницу","Уйти"); break;
        case BUTCHER: page(c,"МЯСНИК","Он разрезает пространство вместе с вашими телами: «Вас скоро спасут, но времени почти не останется».","Не закрывать глаза"); break;
        case RESCUE: page(c,"ВОССТАНОВЛЕНИЕ","Вы снова целы, но таймер почти истёк. Доказательств недостаточно, городские часы начинают бить.","Обвинить парня","Признать провал"); break;
        case FAIL: page(c,"ОШИБОЧНОЕ ОБВИНЕНИЕ","Город не принимает ответ. Портрет рассыпается, а из окон выглядывают одинаковые лица.","Бежать к реке"); break;
        case GHOSTS: page(c,"ГОРОД ПРИЗРАКОВ","Улицы заполняются призраками. Вы находите дощечки и уходите по чёрной воде.","Плыть сквозь туман"); break;
        case COPY: page(c,"КОПИЯ ГОРОДА","За рекой стоит тот же город. На коробке виден знак: четыре фигуры вокруг пустой двери. Игра ждала решения игроков.","Войти во второй город","Искать выход"); break;
        case COPY_HUB: page(c,"ВТОРАЯ ПЕТЛЯ",clues()+"\nВремя ещё не запущено. Можно проверить архив или сломать дверь.","Проверить архив","Разрушить дверь","Обвинить парня"); break;
        case ARCHIVE: page(c,"АРХИВ","Сотни отчётов повторяют одно убийство. Девушка всегда погибает до появления игроков. Подпись: «Смотритель создаёт жертву, чтобы открывался следующий город».","Запомнить имя"); break;
        case ACCUSE: page(c,"ПОСЛЕДНИЙ ВЫБОР","Дверь требует назвать настоящего убийцу. Улики: "+evidence()+"/5.","Парень с фотографии","Мясник","Смотритель коробки"); break;
        case GOOD: end(c,"РАСКРЫТОЕ ДЕЛО","Вы называете Смотрителя. Город складывается внутрь коробки. На берегу вас ждут двое других игроков: они отказались входить и сохранили свободу."); break;
        case BAD: end(c,"ВЕЧНЫЙ ГОРОД","Таймер обнуляется. Вы становитесь жителями города и ждёте новых игроков. Следующая коробка уже открывается."); break;
        case SECRET: end(c,"ОТКАЗ ОТ ИГРЫ","Вы находите швы коробки и выходите наружу. Двое остальных участников поняли раньше: правила действуют только на тех, кто согласился играть."); break;
      }
      if(red){p.setColor(0x66cc0000);c.drawRect(0,0,getWidth(),getHeight(),p);red=false;}
    }

    void title(Canvas c){
      p.setTextAlign(Paint.Align.CENTER);p.setFakeBoldText(true);p.setTextSize(sp(34));p.setColor(0xffeee9da);
      c.drawText("ГОРОД",getWidth()/2f,dp(210),p);c.drawText("В КОРОБКЕ",getWidth()/2f,dp(252),p);
      p.setFakeBoldText(false);p.setTextSize(sp(15));p.setColor(0xff9da5b0);c.drawText("детективный хоррор-квест",getWidth()/2f,dp(286),p);
      wrap(c,"Двое героев входят в игру, где город повторяет одно убийство. Найдите выход до того, как жители запустят последний отсчёт.",dp(28),dp(350),getWidth()-dp(56),sp(17),0xffd2d3cd);
      button(c,"НАЧАТЬ ИГРУ",0,dp(520),1);
      p.setTextSize(sp(13));p.setColor(0xff747c88);c.drawText("Открыто концовок: "+found()+"/3",getWidth()/2f,getHeight()-dp(25),p);
    }

    void page(Canvas c,String t,String body,String... choices){
      p.setTextAlign(Paint.Align.LEFT);p.setFakeBoldText(true);p.setTextSize(sp(22));p.setColor(0xffeee9da);c.drawText(t,dp(22),dp(112),p);p.setFakeBoldText(false);
      wrap(c,body,dp(22),dp(154),getWidth()-dp(44),sp(16),0xffc8cbce);
      for(int i=0;i<choices.length;i++)button(c,choices[i],i,0,choices.length);
    }

    void end(Canvas c,String t,String body){page(c,"КОНЦОВКА: "+t,body,"СЫГРАТЬ СНОВА");}

    void header(Canvas c){
      p.setTextAlign(Paint.Align.LEFT);p.setFakeBoldText(true);p.setTextSize(sp(13));p.setColor(0xffb5bbc3);c.drawText("ПЕТЛЯ "+loop,dp(18),dp(32),p);p.setFakeBoldText(false);
      if(ticking){p.setTextAlign(Paint.Align.RIGHT);p.setTextSize(sp(16));p.setColor(time<16?0xffee5555:0xffe4bc62);c.drawText(String.format("%02d:%02d",time/60,time%60),getWidth()-dp(18),dp(33),p);}
      line.setColor(0x557f8792);c.drawLine(dp(18),dp(49),getWidth()-dp(18),dp(49),line);
    }

    void background(Canvas c){
      p.setShader(new LinearGradient(0,0,0,getHeight(),0xff0b0d12,0xff020306,Shader.TileMode.CLAMP));c.drawRect(0,0,getWidth(),getHeight(),p);p.setShader(null);
      if(scene==RIVER||scene==BOX||scene==GHOSTS||scene==COPY||scene==SECRET){p.setColor(0x553b536a);c.drawRect(0,getHeight()*.62f,getWidth(),getHeight(),p);}
      else if(scene!=TITLE){p.setColor(0x551b2028);for(int i=0;i<9;i++){float x=i*getWidth()/8f-dp(12),hh=dp(75+(i*37)%140);c.drawRect(x,getHeight()*.73f-hh,x+dp(38+(i%3)*10),getHeight()*.73f,p);}}
      line.setColor(0x223f5368);for(int i=0;i<24;i++){float x=(i*97+frame*7)%(getWidth()+80)-40,y=(i*151+frame*23)%(getHeight()+100)-50;c.drawLine(x,y,x-dp(7),y+dp(22),line);}
    }

    void button(Canvas c,String text,int action,float fixed,int count){
      float top=fixed>0?fixed:getHeight()-dp(74)-(count-1-hits.size())*dp(64);RectF r=new RectF(dp(20),top,getWidth()-dp(20),top+dp(54));
      p.setColor(0xdd16191f);c.drawRoundRect(r,dp(8),dp(8),p);line.setColor(0x99878e98);c.drawRoundRect(r,dp(8),dp(8),line);
      p.setTextAlign(Paint.Align.CENTER);p.setFakeBoldText(true);p.setTextSize(sp(14));p.setColor(0xffe4e1d9);c.drawText(text,r.centerX(),r.centerY()+dp(5),p);p.setFakeBoldText(false);hits.add(new Hit(r,action));
    }

    void wrap(Canvas c,String text,float x,float y,float width,float size,int color){
      p.setTextAlign(Paint.Align.LEFT);p.setTextSize(size);p.setColor(color);float yy=y;
      for(String para:text.split("\\n")){String line="";for(String w:para.split(" ")){String n=line.isEmpty()?w:line+" "+w;if(p.measureText(n)>width&&!line.isEmpty()){c.drawText(line,x,yy,p);yy+=dp(24);line=w;}else line=n;}if(!line.isEmpty()){c.drawText(line,x,yy,p);yy+=dp(24);}yy+=dp(7);}
    }

    @Override public boolean onTouchEvent(MotionEvent e){if(e.getAction()!=MotionEvent.ACTION_UP)return true;for(Hit x:hits)if(x.r.contains(e.getX(),e.getY())){act(x.a);invalidate();break;}return true;}

    void act(int a){switch(scene){
      case TITLE: reset();scene=RIVER;break;
      case RIVER: if(a==1)shore=true;scene=BOX;break;
      case BOX: if(a==1)shore=true;else scene=CITY;break;
      case CITY: scene=HUB;break;
      case HUB: scene=a==0?HOUSE:a==1?SQUARE:a==2?SHOP:BUTCHER;break;
      case HOUSE: scene=a==0?ALBUM:HUB;break;
      case ALBUM: if(a==0)photo=true;scene=HUB;break;
      case SQUARE: whisper=true;ticking=true;time=90;scene=HUB;break;
      case SHOP: if(a==0)journal=true;scene=HUB;break;
      case BUTCHER: red=true;if(!ticking){ticking=true;time=45;}time=Math.min(time,45);scene=RESCUE;break;
      case RESCUE: scene=FAIL;break;
      case FAIL: scene=GHOSTS;break;
      case GHOSTS: ticking=false;time=-1;loop=2;scene=COPY;break;
      case COPY: if(a==0)scene=COPY_HUB;else{scene=SECRET;ending("secret");}break;
      case COPY_HUB: if(a==0)scene=ARCHIVE;else if(a==1)scene=ACCUSE;else{scene=BAD;ending("bad");}break;
      case ARCHIVE: archive=true;scene=ACCUSE;break;
      case ACCUSE: if(a==2&&archive&&evidence()>=3){scene=GOOD;ending("good");}else{scene=BAD;ending("bad");}break;
      default: reset();scene=TITLE;
    }}

    String clues(){String s="Улики: ";if(evidence()==0)return "Улик пока нет.";if(shore)s+="знак на берегу; ";if(photo)s+="фотография; ";if(whisper)s+="слова жительницы; ";if(journal)s+="журнал спасения; ";if(archive)s+="архив; ";return s;}
    int evidence(){int n=0;if(shore)n++;if(photo)n++;if(journal)n++;if(whisper)n++;if(archive)n++;return n;}
    void ending(String k){save.edit().putBoolean(k,true).apply();}
    int found(){int n=0;if(save.getBoolean("good",false))n++;if(save.getBoolean("bad",false))n++;if(save.getBoolean("secret",false))n++;return n;}
    void reset(){loop=1;time=-1;ticking=photo=shore=journal=whisper=archive=red=false;}
    float dp(float v){return v*getResources().getDisplayMetrics().density;}
    float sp(float v){return v*getResources().getDisplayMetrics().scaledDensity;}
    static final class Hit{final RectF r;final int a;Hit(RectF r,int a){this.r=r;this.a=a;}}
  }
}
