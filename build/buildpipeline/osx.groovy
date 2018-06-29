@Library('dotnet-ci') _

// 'node' indicates to Jenkins that the enclosed block runs on a node that matches
// the label 'windows-with-vs'
simpleNode('OSX.1012.Amd64.Open') {
    stage ('Checking out source') {
        checkout scm
    }
    stage ('Build') {
        def logFolder = getLogFolder()
        def environment = "export ASPNETCORE_TEST_LOG_DIR=${WORKSPACE}/${logFolder}"
        sh "open --background -a Docker && while ! docker system info > /dev/null 2>&1; do sleep 1; done"
        sh "${environment}&./build.sh -ci"
    }
}
