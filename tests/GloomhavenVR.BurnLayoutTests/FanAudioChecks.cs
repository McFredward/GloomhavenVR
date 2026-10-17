using GloomhavenVR.Cards;

internal static class FanAudioChecks {
    private static int checks;
    private static void Check(bool value,string message) { checks++;if(!value)throw new Exception(message); }

    internal static int Run() {
        // Each frame re-evaluates the actual driver branch. Keep both animated and
        // instant close paths: IsOpen changes before the collapse root disappears.
        foreach(float closeDuration in new[]{.2f,0f}) {
            CardsConfig.FanCloseDuration.Value=closeDuration;
            var driver=new CardsDriver();
            driver.FanEdgeProbe(true);
            Check(driver.FanIsOpen&&driver.FanRootVisible&&driver.FanOpenSounds==1,"A successful initial reveal emits exactly one open cue");
            driver.SetBurnWait(true);
            for(int frame=0;frame<180;frame++) {
                driver.FanEdgeProbe(false);
                Check(driver.FanIsOpen&&driver.FanRootVisible&&driver.FanCloseSounds==0,"Blocked close frames must not emit a hide cue");
            }
            driver.SetBurnWait(false);driver.FanEdgeProbe(false);
            Check(!driver.FanIsOpen&&driver.FanCloseSounds==1,"Burn completion admits exactly one hide cue");
            Check(closeDuration>0f?driver.FanCollapsing&&driver.FanRootVisible:!driver.FanRootVisible,"Successful close keeps the original animated or instant hierarchy behavior");
            for(int frame=0;frame<180;frame++) driver.FanEdgeProbe(false);
            Check(driver.FanCloseSounds==1,"Stable closed frames must not replay the hide cue");
            driver.SetBurnWait(true);
            for(int frame=0;frame<180;frame++) {
                driver.FanEdgeProbe(true);
                Check(!driver.FanIsOpen&&driver.FanOpenSounds==1,"Blocked open frames must not emit a reveal cue");
            }
            driver.SetBurnWait(false);driver.FanEdgeProbe(true);
            Check(driver.FanIsOpen&&driver.FanOpenSounds==2,"Burn completion admits exactly one reveal cue");
            for(int frame=0;frame<180;frame++) driver.FanEdgeProbe(true);
            Check(driver.FanOpenSounds==2,"Stable open frames must not replay the reveal cue");
            driver.FanEdgeProbe(false);driver.FanEdgeProbe(true);driver.FanEdgeProbe(false);
            Check(driver.FanOpenSounds==3&&driver.FanCloseSounds==3,"Later ordinary reopen and close edges keep their original cues");
        }
        {
            var driver=new CardsDriver();driver.FanEdgeProbe(true);driver.SetBurnWait(true);
            for(int frame=0;frame<180;frame++) driver.FanEdgeProbe(false);
            driver.FanEdgeProbe(true);driver.SetBurnWait(false);driver.FanEdgeProbe(true);
            Check(driver.FanIsOpen&&driver.FanOpenSounds==1&&driver.FanCloseSounds==0,"Cancelled close intent must never produce a later hide cue");
        }
        {
            var driver=new CardsDriver();driver.SetBurnWait(true);
            for(int frame=0;frame<180;frame++) driver.FanEdgeProbe(true);
            driver.FanEdgeProbe(false);driver.SetBurnWait(false);driver.FanEdgeProbe(false);
            Check(!driver.FanIsOpen&&driver.FanOpenSounds==0&&driver.FanCloseSounds==0,"Cancelled open intent must never produce a later reveal cue");
            driver.FanEdgeProbe(true);driver.FanEdgeProbe(false);
            Check(driver.FanOpenSounds==1&&driver.FanCloseSounds==1,"Cancelled intent must not suppress a later real edge");
        }
        return checks;
    }
}
