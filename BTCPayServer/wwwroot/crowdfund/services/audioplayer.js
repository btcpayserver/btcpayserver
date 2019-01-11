

function playSound(path) {
    // audio supported?
    if (typeof window.Audio === 'function') {
        var audioElem = new Audio(path);

        audioElem.play().catch(function(){
            debugger;
        })
    }
}


function playQuakeSound (name){
    // var path = window.location.protocol +"://github.com/ClaudiuHKS/AdvancedQuakeSounds/blob/master/sound/QuakeSounds/"+name+"?raw=true"
    var path = window.location.protocol + "//github.com/ClaudiuHKS/AdvancedQuakeSounds/raw/master/sound/QuakeSounds/" + name;
    playSound(path);
}

function playRandomQuakeSound(){
    playQuakeSound(quake[Math.floor((Math.random() * quake.length) )]);
}

var quake = [
"dominating.wav"
,"doublekill.wav"
,"doublekill2.wav"
,"eagleeye.wav"
,"firstblood.wav"
,"firstblood2.wav"
,"firstblood3.wav"
,"flawless.wav"
,"godlike.wav"
,"hattrick.wav"
,"headhunter.wav"
,"headshot.wav"
,"headshot2.wav"
,"headshot3.wav"
,"holyshit.wav"
,"killingspree.wav"
,"knife.wav"
,"knife2.wav"
,"knife3.wav"
,"ludicrouskill.wav"
,"megakill.wav"
,"monsterkill.wav"
,"multikill.wav"
,"nade.wav"
,"ownage.wav"
,"payback.wav"
,"prepare.wav"
,"prepare2.wav"
,"prepare3.wav"
,"prepare4.wav"
,"rampage.wav"
,"suicide.wav"
,"suicide2.wav"
,"suicide3.wav"
,"suicide4.wav"
,"teamkiller.wav"
,"triplekill.wav"
,"ultrakill.wav"
,"unstoppable.wav"
,"whickedsick.wav"];
