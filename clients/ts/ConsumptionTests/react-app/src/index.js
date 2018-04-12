import React from 'react';
import ReactDOM from 'react-dom';
import './index.css';
import App from './App';
import registerServiceWorker from './registerServiceWorker';

import { HubConnection } from '@aspnet/signalr'
import { MessagePackHubProtocol } from '@aspnet/signalr-protocol-msgpack'

const connection = new HubConnection("/", {
    protocol: new MessagePackHubProtocol()
});

ReactDOM.render(<App />, document.getElementById('root'));
registerServiceWorker();
