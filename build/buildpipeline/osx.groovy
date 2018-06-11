@Library('dotnet-ci') _

// 'node' indicates to Jenkins that the enclosed block runs on a node that matches
// the label 'windows-with-vs'
simpleNode('OSX10.13.Amd64.Open') {
    stage ('Checking out source') {
        checkout scm
    }
    stage ('Build') {
        def logFolder = getLogFolder()
        def environment = "export ASPNETCORE_TEST_LOG_DIR=${WORKSPACE}\\${logFolder}"
        bat "${environment}&.\\run.sh --ci default-build"
    }
}
