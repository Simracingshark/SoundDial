#include "LedRing.h"
#include <driver/rmt.h>

static void rainbowColor(uint8_t position,uint8_t& red,uint8_t& green,uint8_t& blue) {
  if(position<85){red=255-position*3;green=position*3;blue=0;}
  else if(position<170){position-=85;red=0;green=255-position*3;blue=position*3;}
  else{position-=170;red=position*3;green=0;blue=255-position*3;}
}

void LedRing::begin() {
  rmt_config_t config = RMT_DEFAULT_CONFIG_TX(static_cast<gpio_num_t>(Hardware::PIN_WS2812), RMT_CHANNEL_0);
  config.clk_div = 8; rmt_config(&config); rmt_driver_install(config.channel, 0, 0); off();
}

void LedRing::send(const uint8_t pixels[Hardware::LED_COUNT][3]) {
  rmt_item32_t items[Hardware::LED_COUNT*24+1]; size_t index=0;
  for(uint8_t p=0;p<Hardware::LED_COUNT;++p) {
    const uint8_t order[3]={pixels[p][1],pixels[p][0],pixels[p][2]};
    for(uint8_t c=0;c<3;++c) for(int8_t bit=7;bit>=0;--bit) {
      const bool one=order[c]&(1u<<bit);
      items[index++]=one ? rmt_item32_t{{{8,1,4,0}}} : rmt_item32_t{{{4,1,8,0}}};
    }
  }
  items[index++]=rmt_item32_t{{{0,0,600,0}}}; rmt_write_items(RMT_CHANNEL_0,items,index,true);
}

void LedRing::render(uint8_t volume,bool muted,bool connected,uint8_t red,uint8_t green,uint8_t blue,uint8_t brightness,uint8_t mode,uint8_t rainbowSpeed,bool rainbowReverse,uint8_t rainbowSpread) {
  uint8_t px[Hardware::LED_COUNT][3]{};
  if(mode==2){send(px);return;}
  if(!connected){red=255;green=75;blue=0;}
  else if(muted){
    const uint8_t level=(millis()/450)%2?brightness:brightness/8;
    for(uint8_t i=0;i<Hardware::LED_COUNT;++i)px[i][0]=level;
    send(px);return;
  }
  if(mode==3&&connected&&!muted){
    const uint8_t divisors[]={38,18,8};uint8_t phase=millis()/divisors[min(rainbowSpeed,(uint8_t)2)];if(rainbowReverse)phase=255-phase;
    for(uint8_t i=0;i<Hardware::LED_COUNT;++i){uint8_t rr,gg,bb;rainbowColor(phase+i*rainbowSpread,rr,gg,bb);px[i][0]=rr*brightness/255;px[i][1]=gg*brightness/255;px[i][2]=bb*brightness/255;}
    send(px);return;
  }
  const float lit = mode==1 ? Hardware::LED_COUNT : volume*Hardware::LED_COUNT/100.0f;
  for(uint8_t i=0;i<Hardware::LED_COUNT;++i) {
    float amount=constrain(lit-i,0.0f,1.0f); px[i][0]=red*brightness*amount/255; px[i][1]=green*brightness*amount/255; px[i][2]=blue*brightness*amount/255;
  }
  send(px);
}

void LedRing::renderNetwork(uint8_t status,uint8_t brightness,uint8_t mode) {
  uint8_t px[Hardware::LED_COUNT][3]{};
  if(mode==2||brightness==0){send(px);return;}
  const uint8_t frame=(millis()/180)%Hardware::LED_COUNT;
  if(status==5){
    if((millis()/400)%2)for(uint8_t i=0;i<Hardware::LED_COUNT;++i)px[i][0]=brightness;
  }else{
    uint8_t red=0,green=0,blue=255;
    if(status==1){green=210;blue=255;}       // scanning: cyan
    else if(status==2){green=90;blue=255;}   // setup AP: blue
    else if(status==3){green=40;blue=255;}   // joining Wi-Fi: deep blue
    else if(status==4){red=160;blue=255;}    // finding PC: purple
    for(uint8_t i=0;i<Hardware::LED_COUNT;++i){const uint8_t level=i==frame?brightness:brightness/10;px[i][0]=red*level/255;px[i][1]=green*level/255;px[i][2]=blue*level/255;}
  }
  send(px);
}
void LedRing::off(){uint8_t px[Hardware::LED_COUNT][3]{};send(px);}
