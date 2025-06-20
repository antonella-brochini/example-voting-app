var express = require('express'),
    async = require('async'),
    { Pool } = require('pg'),
    cookieParser = require('cookie-parser'),
    app = express(),
    server = require('http').Server(app),
    io = require('socket.io')(server);

const axios = require('axios');
const path = require('path');
var port = process.env.PORT || 80;

io.on('connection', function (socket) {

  socket.emit('message', { text : 'Welcome!' });

  socket.on('subscribe', function (data) {
    socket.join(data.channel);
  });
});

var pool = new Pool({
  connectionString: 'postgres://postgres:postgres@db/postgres'
});

async.retry(
  {times: 1000, interval: 1000},
  function(callback) {
    pool.connect(function(err, client, done) {
      if (err) {
        console.error("Waiting for db");
      }
      callback(err, client);
    });
  },
  function(err, client) {
    if (err) {
      return console.error("Giving up");
    }
    console.log("Connected to db");
    getVotes(client);
  }
);

function getVotes(client) {
  client.query('SELECT vote, COUNT(id) AS count FROM votes GROUP BY vote', [], function(err, result) {
    if (err) {
      console.error("Error performing query: " + err);
    } else {
      var votes = collectVotesFromResult(result);
      io.sockets.emit("scores", JSON.stringify(votes));
    }

    setTimeout(function() {getVotes(client) }, 1000);
  });
}

function collectVotesFromResult(result) {
  var votes = {a: 0, b: 0};

  result.rows.forEach(function (row) {
    votes[row.vote] = parseInt(row.count);
  });

  return votes;
}

app.use(cookieParser());
app.use(express.urlencoded());
app.use(express.static(__dirname + '/views'));

app.get('/', function (req, res) {
  res.sendFile(path.resolve(__dirname + '/views/index.html'));
});

server.listen(port, function () {
  var port = server.address().port;
  console.log('App running on port ' + port);
});


/////////////////////////////mi codigo/////////////////////////////

setTimeout(async () => {
  try {
    // Obtener los votos desde la base de datos
    const res = await pool.query('SELECT vote, COUNT(id) AS count FROM votes GROUP BY vote');
    const votos = collectVotesFromResult(res);

    // Llamar a la API Gateway con los votos
    const url = 'https://yyje8drild.execute-api.us-east-1.amazonaws.com/prod/alerta'; // Tu endpoint
    await axios.post(url, {
      candidate: 'todos', // o null
      votes: votos,
      env: 'prod'
    }, {
      headers: {
        'Content-Type': 'application/json'
      }
    });

    console.log('📤 Backup de resultados enviado a Lambda');
  } catch (err) {
    console.error('❌ Error al enviar backup:', err.message);
  }
}, 10 * 60 * 1000); // 10 minutos