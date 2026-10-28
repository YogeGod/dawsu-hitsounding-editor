'use strict';
function apply(window,action){
 switch(action){
  case 'close':window.close();return {action};
  case 'minimize':window.minimize();return {action};
  case 'maximize':if(window.isMaximized())window.unmaximize();else window.maximize();return {action,maximized:window.isMaximized()};
  default:throw Error('Unknown window action.');
 }
}
module.exports={apply};
