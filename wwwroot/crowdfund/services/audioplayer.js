function playSound(path) {
    // audio supported?
    if (typeof window.Audio === 'function') {
        var audioElem = new Audio(path);

        audioElem.play().catch(function(){
            debugger;
        })
    }
}

function playRandomSound(){
    var sound  =  srvModel.sounds[Math.floor((Math.random() *  srvModel.sounds.length) )];
    playSound(sound);
}
